using Chromatics.Core;
using Chromatics.Extensions;
using Chromatics.Extensions.RGB.NET;
using Chromatics.Extensions.RGB.NET.Decorators;
using Chromatics.Extensions.Sharlayan;
using Chromatics.Helpers;
using Chromatics.Interfaces;
using Chromatics.Models;
using Cyotek.Windows.Forms;
using FFXIVWeather;
using NLog.Config;
using RGB.NET.Core;
using RGB.NET.Presets.Decorators;
using RGB.NET.Presets.Textures;
using RGB.NET.Presets.Textures.Gradients;
using Sharlayan;
using Sharlayan.Core;
using Sharlayan.Models.ReadResults;
using Sharlayan.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static MetroFramework.Drawing.MetroPaint;

namespace Chromatics.Layers
{
    public class ReactiveWeatherProcessor : LayerProcessor
    {
        private static ReactiveWeatherProcessor _instance;
        private static Dictionary<int, ReactiveWeatherBaseModel> layerProcessorModel = new Dictionary<int, ReactiveWeatherBaseModel>();

        internal static int _previousArrayIndex = 0;
        internal static int _previousOffset = 0;
        internal static bool dutyComplete = false;
        internal static bool raidEffectsRunning = false;
        internal static string[] bossNames = null;

        private SolidColorBrush weather_brush;
        private bool _disposed = false;


        // Private constructor to prevent direct instantiation
        private ReactiveWeatherProcessor() { }

        // Singleton instance access
        public static ReactiveWeatherProcessor Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ReactiveWeatherProcessor();
                }
                return _instance;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    foreach (var model in layerProcessorModel.Values)
                    {
                        // Clear the gradient effects for each model
                        if (model._gradientEffects != null)
                        {
                            foreach (var gradient in model._gradientEffects)
                            {
                                gradient.RemoveAllDecorators();
                            }
                            model._gradientEffects.Clear();
                        }
                    }

                    weather_brush = null;

                    layerProcessorModel.Clear();
                }

                _disposed = true;
            }

            // Call base class implementation to clean up base resources
            base.Dispose(disposing);
            _instance = null;
        }

        public override void Process(IMappingLayer layer)
        {
            if (RGBController.IsBaseLayerEffectRunning())
            {
                //StopEffects(layergroup);
                return;
            }

            ReactiveWeatherBaseModel model;

            if (!layerProcessorModel.ContainsKey(layer.layerID))
            {
                model = new ReactiveWeatherBaseModel();
                layerProcessorModel.Add(layer.layerID, model);
            }
            else
            {
                model = layerProcessorModel[layer.layerID];
            }

            //Reactive Weather Base Layer Implementation
            var _colorPalette = RGBController.GetActivePalette();
            var reactiveWeatherEffects = RGBController.GetEffectsSettings().effect_reactiveweather;
            var raidEffects = RGBController.GetEffectsSettings().effect_raideffects;

            var weather_color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherUnknownBase.Color);

            weather_brush = new SolidColorBrush(weather_color);

            var _layergroups = RGBController.GetLiveLayerGroups();
            var effectApplied = false;

            var weatherService = FFXIVWeatherExtensions.GetWeatherService();
            if (weatherService == null) return;

            ListLedGroup layergroup;
            var ledArray = GetLedArray(layer);

            if (_layergroups.ContainsKey(layer.layerID))
            {
                layergroup = _layergroups[layer.layerID].FirstOrDefault();
            }
            else
            {
                layergroup = new ListLedGroup(surface, ledArray)
                {
                    ZIndex = layer.zindex,
                };

                var lg = new ListLedGroup[] { layergroup };
                _layergroups.Add(layer.layerID, lg);

                layergroup.Brush = weather_brush;
                layergroup.Detach();
            }

            if (model._reactiveWeatherEffects != reactiveWeatherEffects)
            {
                if (!reactiveWeatherEffects)
                {
                    StopEffects(layergroup, model._gradientEffects);
                    
                }
            }

            if (model._raidEffects != raidEffects)
            {
                if (!raidEffects)
                {
                    StopEffects(layergroup, model._gradientEffects);
                }
            }

            if (!layer.Enabled)
            {
                StopEffects(layergroup, model._gradientEffects);
                weather_brush.Color = ColorHelper.ColorToRGBColor(System.Drawing.Color.Black);
                layergroup.Brush = weather_brush;
            }
            else
            {
                // Process data from FFXIV
                var _memoryHandler = GameController.GetGameData();

                if (_memoryHandler?.Reader != null && _memoryHandler.Reader.CanGetActors())
                {
                    var getCurrentPlayer = _memoryHandler.Reader.GetCurrentPlayer();
                    if (getCurrentPlayer.Entity == null) return;

                    var currentZone = GameHelper.GetZoneNameById(getCurrentPlayer.Entity.MapTerritory);

                    DutyFinderBellExtension.CheckCache();
                    WeatherExtension.CheckCache();

                    ChatLogResult readResult = _memoryHandler.Reader.GetChatLog(_previousArrayIndex, _previousOffset);

                    if (readResult.PreviousArrayIndex != _previousArrayIndex)
                    {
                        var chatLogEntries = readResult.ChatLogItems;

                        if (chatLogEntries.Count > 0)
                        {
                            if (chatLogEntries.First().Code == "0840" && Regex.IsMatch(chatLogEntries.First().Message, @"completion time: (\d+:\d+)", RegexOptions.IgnoreCase))
                            {
                                dutyComplete = true;
                                raidEffectsRunning = false;
                                bossNames = null;

                            }
                            else if (chatLogEntries.First().Code == "0839" && Regex.IsMatch(chatLogEntries.First().Message, @"has begun\.", RegexOptions.IgnoreCase))
                            {
                                dutyComplete = false;
                            }
                            else if (chatLogEntries.First().Code == "083E" && Regex.IsMatch(chatLogEntries.First().Message, @"You obtain \d+ Allagan tomestones of \w+\.", RegexOptions.IgnoreCase))
                            {
                                dutyComplete = true;
                                raidEffectsRunning = false;
                                bossNames = null;
                            }
                            else if (chatLogEntries.First().Code == "0839" && Regex.IsMatch(chatLogEntries.First().Message, @"has ended\.", RegexOptions.IgnoreCase))
                            {
                                dutyComplete = true;
                                raidEffectsRunning = false;
                                bossNames = null;
                            }
                            else if (bossNames != null && (chatLogEntries.First().Code == "133A" || chatLogEntries.First().Code == "0B3A") && Regex.IsMatch(chatLogEntries.First().Message, @"(.* defeats|You defeat|You defeat the) (" + string.Join("|", bossNames.Select(Regex.Escape)) + @")\.", RegexOptions.IgnoreCase))
                            {
                                dutyComplete = true;
                                raidEffectsRunning = false;
                                bossNames = null;
                            }
                        }

                        _previousArrayIndex = readResult.PreviousArrayIndex;
                        _previousOffset = readResult.PreviousOffset;
                    }

                    if (currentZone != "???" && currentZone != "")
                    {
                        var currentWeatherZone = WeatherExtension.WeatherId();
                        var currentWeather = weatherService.GetCurrentWeather(currentZone).Item1.ToString();

                        if (currentWeather == null)
                        {
                            currentWeather = weatherService.GetCurrentWeather(currentZone).Item1.ToString();
                        }

                        if ((model._currentWeather != currentWeather || model._currentZone != currentZone || model._reactiveWeatherEffects != reactiveWeatherEffects || model._raidEffects != raidEffects || layer.requestUpdate || model._inInstance != DutyFinderBellExtension.InInstance() || model._dutyComplete != dutyComplete) && currentWeather != "CutScene")
                        {
                            effectApplied = SetReactiveWeather(layer, layergroup, currentZone, currentWeather, weather_brush, _colorPalette, surface, ledArray, model._gradientEffects, DutyFinderBellExtension.InInstance(), layer.deviceGuid);

                            model._currentWeather = currentWeather;
                            model._currentZone = currentZone;
                            model._inInstance = DutyFinderBellExtension.InInstance();
                            model._dutyComplete = dutyComplete;


                        }
                    }
                }
            }

            // Apply lighting
            if (model._reactiveWeatherEffects != reactiveWeatherEffects)
            {
                model._reactiveWeatherEffects = reactiveWeatherEffects;
            }

            if (model._raidEffects != raidEffects)
            {
                model._raidEffects = raidEffects;
            }

            if (!effectApplied && layergroup.Decorators.Count <= 0)
            {
                layergroup.Attach(surface);
            }

            _init = true;
            layer.requestUpdate = false;
        }

        private class ReactiveWeatherBaseModel
        {
            public HashSet<LinearGradient> _gradientEffects { get; set; } = new HashSet<LinearGradient>();
            public string _currentWeather { get; set; }
            public string _currentZone { get; set; }
            public bool _reactiveWeatherEffects { get; set; }
            public bool _raidEffects { get; set; }
            public bool _inInstance { get; set; }
            public bool _dutyComplete { get; set; }

        }


        private static void SetEffect(ILedGroupDecorator effect, ListLedGroup layer, List<ListLedGroup> runningEffects)
        {
            if (runningEffects.Contains(layer))
                runningEffects.Remove(layer);

            layer.RemoveAllDecorators();
            layer.AddDecorator(effect);

            runningEffects.Add(layer);
        }

        private static void SetLinearGradientEffect(LinearGradient gradient, IGradientDecorator effect, ListLedGroup layer, Size boundry, List<ListLedGroup> runningEffects, HashSet<LinearGradient> _gradientEffects)
        {
            if (runningEffects.Contains(layer))
                runningEffects.Remove(layer);

            layer.RemoveAllDecorators();
            gradient.WrapGradient = true;
            gradient.AddDecorator(effect);

            layer.Brush = new TextureBrush(new LinearGradientTexture(boundry, gradient));

            runningEffects.Add(layer);
            _gradientEffects.Add(gradient);
        }
        private static void SetRadialGradientEffect(LinearGradient gradient, IGradientDecorator effect, ListLedGroup layer, Size boundry, List<ListLedGroup> runningEffects, HashSet<LinearGradient> _gradientEffects)
        {
            if (runningEffects.Contains(layer))
                runningEffects.Remove(layer);

            layer.RemoveAllDecorators();
            gradient.WrapGradient = true;
            gradient.AddDecorator(effect);

            layer.Brush = new TextureBrush(new ConicalGradientTexture(boundry, gradient));

            runningEffects.Add(layer);
            _gradientEffects.Add(gradient);
        }

        private static void StopEffects(ListLedGroup layer, HashSet<LinearGradient> _gradientEffects)
        {
            var runningEffects = RGBController.GetRunningEffects();

            if (runningEffects.Contains(layer))
                runningEffects.Remove(layer);

            foreach(var gradient in _gradientEffects)
            {
                gradient.RemoveAllDecorators();
            }

            layer.RemoveAllDecorators();
        }

        private static bool SetReactiveWeather(IMappingLayer masterlayer, ListLedGroup layer, string zone, string weather, SolidColorBrush weather_brush, PaletteColorModel _colorPalette, RGBSurface surface, Led[] ledArray, HashSet<LinearGradient> _gradientEffects, bool inInstance, Guid deviceGuid)
        {
            var effectSettings = RGBController.GetEffectsSettings();
            var runningEffects = RGBController.GetRunningEffects();
            var reactiveWeatherEffects = effectSettings.effect_reactiveweather;
            var color = GetWeatherColor(weather, _colorPalette);

            //Filter for zone specific special weather
            //Logger.WriteConsole(Enums.LoggerTypes.FFXIV, $"Zone: {zone}. DFC: {dutyComplete}. InInstance: {inInstance}");

            if (inInstance && !dutyComplete)
            {
                switch (zone)
                {
                    case "Summit of Everkeep":
                        //Raid Zone Effect
                        if (effectSettings.effect_raideffects && !raidEffectsRunning)
                        {

                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectEverkeepBase.Color);
                            var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectEverkeepHighlight1.Color), ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectEverkeepHighlight2.Color), ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectEverkeepHighlight3.Color) };
                            //var starfield = new FastStarfieldDecorator(layer, layer.Count() / 4, 20, 80, animationCol, surface, 2.0, false, baseCol);
                            var starfield = new BPMFastStarfieldDecorator(layer, layer.Count() / 6, 198, 80, animationCol, surface, 2.0, false, baseCol);

                            layer.Brush = new SolidColorBrush(baseCol);
                            SetEffect(starfield, layer, runningEffects);

                            raidEffectsRunning = true;
                            bossNames = ["Zoraal Ja"];

                            return true;
                        }
                        break;
                    case "Interphos":

                        //Raid Zone Effect
                        if (effectSettings.effect_raideffects && !raidEffectsRunning)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectInterphosBase.Color);

                            var animationCol1 = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectInterphosHighlight1.Color);
                            var animationCol2 = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectInterphosHighlight2.Color);
                            var animationCol3 = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectInterphosHighlight3.Color);

                            var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol),
                                new GradientStop((float)0.20, animationCol1),
                                new GradientStop((float)0.35, animationCol2),
                                new GradientStop((float)0.50, animationCol3),
                                new GradientStop((float)0.65, animationCol1),
                                new GradientStop((float)0.80, animationCol2),
                                new GradientStop((float)0.95, animationCol3));

                            var gradientMove = new MoveGradientDecorator(surface, 180, true);

                            SetRadialGradientEffect(animationGradient, gradientMove, layer, new Size(100, 100), runningEffects, _gradientEffects);

                            runningEffects.Add(layer);
                            raidEffectsRunning = true;
                            bossNames = ["Queen Eternal"];

                            return true;
                        }
                        break;
                    case "Scratching Ring":
                        //Raid Zone Effect
                        if (effectSettings.effect_raideffects && !raidEffectsRunning)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM1Base.Color);

                            var animationCol1 = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM1Highlight1.Color);
                            var animationCol2 = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM1Highlight2.Color);
                            var animationCol3 = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM1Highlight3.Color);

                            var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol),
                                new GradientStop((float)0.20, animationCol1),
                                new GradientStop((float)0.35, animationCol2),
                                new GradientStop((float)0.50, animationCol3),
                                new GradientStop((float)0.65, animationCol1),
                                new GradientStop((float)0.80, animationCol2),
                                new GradientStop((float)0.95, animationCol3));

                            //var gradientMove = new MoveGradientDecorator(surface, 180, true);
                            var gradientMove = new MoveBPMGradientDecorator(surface, 125 / 4, true);

                            SetRadialGradientEffect(animationGradient, gradientMove, layer, new Size(100, 100), runningEffects, _gradientEffects);

                            runningEffects.Add(layer);
                            raidEffectsRunning = true;
                            bossNames = ["Black Cat"];

                            return true;
                        }
                        break;
                    case "Lovely Lovering":
                        //Raid Zone Effect
                        if (effectSettings.effect_raideffects && !raidEffectsRunning)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM2Base.Color);
                            var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM2Highlight1.Color), ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM2Highlight2.Color) };
                            var arenaLightShow = new ArenaLightShowDecorator(layer, 20, 3.0, 1.0, animationCol, surface, false, baseCol);

                            layer.Brush = new SolidColorBrush(baseCol);
                            SetEffect(arenaLightShow, layer, runningEffects);
                            raidEffectsRunning = true;
                            bossNames = ["Honey B. Lovely"];

                            return true;
                        }
                        break;
                    case "Blasting Ring":
                        //Raid Zone Effect
                        if (effectSettings.effect_raideffects && !raidEffectsRunning)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM3Base.Color);

                            var animationCol1 = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM3Highlight1.Color);
                            var animationCol2 = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM3Highlight2.Color);
                            var animationCol3 = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM3Highlight3.Color);

                            var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol),
                                new GradientStop((float)0.20, animationCol1),
                                new GradientStop((float)0.35, animationCol2),
                                new GradientStop((float)0.50, animationCol3),
                                new GradientStop((float)0.65, animationCol1),
                                new GradientStop((float)0.80, animationCol2),
                                new GradientStop((float)0.95, animationCol3));

                            var gradientMove = new MoveBPMDiagonalGradientDecorator(surface, 110 / 4, DiagonalDirection.TopLeftToBottomRight);

                            SetLinearGradientEffect(animationGradient, gradientMove, layer, new Size(100, 100), runningEffects, _gradientEffects);

                            runningEffects.Add(layer);
                            raidEffectsRunning = true;
                            bossNames = ["Brute Bomber"];

                            return true;
                        }
                        break;
                    case "The Thundering":
                        //Raid Zone Effect
                        if (effectSettings.effect_raideffects && !raidEffectsRunning)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM4Base.Color);
                            var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM4Highlight1.Color), ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM4Highlight2.Color), ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM4Highlight3.Color), ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectM4Highlight4.Color) };
                            var bpmArenaLightShow = new BPMPWMDecorator(layer, 162, 1.0, animationCol, 0.10, 4, surface, false, baseCol);

                            layer.Brush = new SolidColorBrush(baseCol);
                            SetEffect(bpmArenaLightShow, layer, runningEffects);
                            raidEffectsRunning = true;
                            bossNames = ["Wicked Thunder"];

                            return true;
                        }
                        break;
                    case "Sphere of Naught":
                    //case "Dalamud's Shadow":
                        //Raid Zone Effect
                        if (effectSettings.effect_raideffects && !raidEffectsRunning)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectCoDBase.Color);
                            var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectCoDHighlight1.Color), ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectCoDHighlight2.Color), ColorHelper.ColorToRGBColor(_colorPalette.RaidEffectCoDHighlight3.Color) };
                            var arenaLightShow = new ArenaLightShowDecorator(layer, 20, 3.0, 1.0, animationCol, surface, false, baseCol);

                            layer.Brush = new SolidColorBrush(baseCol);
                            SetEffect(arenaLightShow, layer, runningEffects);
                            raidEffectsRunning = true;
                            bossNames = ["Cloud of Darkness", "Rafflesia"];

                            masterlayer.requestUpdate = true;
                            
                            return true;
                        }
                        break;
                }

                return false;
            }
            else
            {
                raidEffectsRunning = false;
                bossNames = null;

                switch (zone)
                {
                    case "Mare Lamentorum":
                        if (weather == "Fair Skies" || weather == "Moon Dust")
                        {
                            if (reactiveWeatherEffects && effectSettings.weather_marelametorum_animation)
                            {
                                var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherMoonDustAnimationBase.Color);
                                var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.WeatherMoonDustAnimationHighlight.Color) };
                                var starfield = new StarfieldDecorator(layer, (layer.Count() / 4), 20, 900, animationCol, surface, false, baseCol);

                                layer.Brush = new SolidColorBrush(baseCol);
                                SetEffect(starfield, layer, runningEffects);

                                return true;
                            }
                            else
                            {
                                color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherMoonDustBase.Color);
                            }
                        }
                        else if (weather == "Umbral Wind")
                        {
                            if (reactiveWeatherEffects && effectSettings.weather_marelametorum_umbralwind_animation)
                            {
                                var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherMoonDustAnimationBase.Color);
                                var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.WeatherMoonDustAnimationHighlight.Color), ColorHelper.ColorToRGBColor(_colorPalette.WeatherMoonDustAnimationHighlight.Color), ColorHelper.ColorToRGBColor(_colorPalette.WeatherMoonDustAnimationHighlight.Color), ColorHelper.ColorToRGBColor(_colorPalette.WeatherUmbralWindAnimationHighlight.Color) };
                                var starfield = new StarfieldDecorator(layer, (layer.Count() / 4), 20, 800, animationCol, surface, false, baseCol);

                                layer.Brush = new SolidColorBrush(baseCol);
                                SetEffect(starfield, layer, runningEffects);

                                return true;
                            }
                            else
                            {
                                color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherMoonDustBase.Color);
                            }
                        }
                        break;
                    case "Ultima Thule":
                        if (weather == "Fair Skies")
                        {
                            if (reactiveWeatherEffects && effectSettings.weather_ultimathule_animation)
                            {
                                var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherUltimaThuleAnimationBase.Color);
                                var starfieldCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.WeatherUltimaThuleAnimationHighlight1.Color), ColorHelper.ColorToRGBColor(_colorPalette.WeatherUltimaThuleAnimationHighlight2.Color), ColorHelper.ColorToRGBColor(_colorPalette.WeatherUltimaThuleAnimationHighlight3.Color), ColorHelper.ColorToRGBColor(_colorPalette.WeatherUltimaThuleAnimationHighlight4.Color) };

                                var starfield = new StarfieldDecorator(layer, (layer.Count() / 4), 20, 500, starfieldCol, surface, false, baseCol);
                                layer.Brush = new SolidColorBrush(baseCol);

                                SetEffect(starfield, layer, runningEffects);

                                return true;
                            }
                            else
                            {
                                color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherUltimaThuleAnimationBase.Color);
                            }
                        }
                        else if (weather == "Astromagnetic Storm" && effectSettings.weather_astromagneticstorm_animation)
                        {
                            if (reactiveWeatherEffects)
                            {
                                var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherAstromagneticStormBase.Color);

                                var animationCol1 = ColorHelper.ColorToRGBColor(_colorPalette.WeatherAstromagneticStormHighlight1.Color);
                                var animationCol2 = ColorHelper.ColorToRGBColor(_colorPalette.WeatherAstromagneticStormHighlight2.Color);
                                var animationCol3 = ColorHelper.ColorToRGBColor(_colorPalette.WeatherAstromagneticStormHighlight3.Color);

                                var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol),
                                    new GradientStop((float)0.20, animationCol1),
                                    new GradientStop((float)0.35, animationCol2),
                                    new GradientStop((float)0.50, animationCol3),
                                    new GradientStop((float)0.65, animationCol1),
                                    new GradientStop((float)0.80, animationCol2),
                                    new GradientStop((float)0.95, animationCol3));

                                var gradientMove = new MoveGradientDecorator(surface, 120, true);

                                SetRadialGradientEffect(animationGradient, gradientMove, layer, new Size(100, 100), runningEffects, _gradientEffects);

                                runningEffects.Add(layer);

                                return true;
                            }
                            else
                            {
                                color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherAstromagneticStormBase.Color);
                            }
                        }
                        else if (weather == "Umbral Wind" && effectSettings.weather_ultimathule_umbralwind_animation)
                        {
                            if (reactiveWeatherEffects)
                            {
                                //return;
                            }
                        }
                        break;
                }

                //Filter for special weather
                switch (weather)
                {
                    case "Rain":
                        if (reactiveWeatherEffects && effectSettings.weather_rain_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherRainBase.Color);
                            var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.WeatherRainAnimation.Color) };
                            var starfield = new StarfieldDecorator(layer, (layer.Count() / 4), 20, 200, animationCol, surface, false, baseCol);

                            layer.Brush = new SolidColorBrush(baseCol);
                            SetEffect(starfield, layer, runningEffects);

                            return true;
                        }
                        else
                        {
                            color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherRainBase.Color);
                        }
                        break;
                    case "Showers":
                        if (reactiveWeatherEffects && effectSettings.weather_showers_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherShowersBase.Color);
                            var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.WeatherShowersHighlight.Color) };
                            var starfield = new StarfieldDecorator(layer, (layer.Count() / 4), 20, 100, animationCol, surface, false, baseCol);

                            layer.Brush = new SolidColorBrush(baseCol);
                            SetEffect(starfield, layer, runningEffects);

                            return true;
                        }
                        else
                        {
                            color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherShowersBase.Color);
                        }
                        break;
                    case "Wind":
                        if (reactiveWeatherEffects && effectSettings.weather_wind_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherWindBase.Color);
                            var animationCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherWindAnimation.Color);

                            var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol), new GradientStop((float)0.25, animationCol), new GradientStop((float)0.75, baseCol), new GradientStop((float)1, animationCol));
                            var gradientMove = new MoveGradientDecorator(surface, 180, true);

                            SetLinearGradientEffect(animationGradient, gradientMove, layer, new Size(100, 100), runningEffects, _gradientEffects);

                            return true;
                        }
                        else
                        {
                            color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherWindBase.Color);
                        }
                        break;
                    case "Gales":
                        if (reactiveWeatherEffects && effectSettings.weather_gales_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherGalesBase.Color);
                            var animationCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherGalesAnimation.Color);

                            var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol), new GradientStop((float)0.25, animationCol), new GradientStop((float)0.75, baseCol), new GradientStop((float)1, animationCol));
                            var gradientMove = new MoveGradientDecorator(surface, 220, true);

                            SetLinearGradientEffect(animationGradient, gradientMove, layer, new Size(100, 100), runningEffects, _gradientEffects);

                            return true;
                        }
                        else
                        {
                            color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherGalesBase.Color);
                        }
                        break;
                    case "Dust Storms":
                    case "Sandstorms":
                        if (reactiveWeatherEffects && effectSettings.weather_sandstorms_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherSandstormsBase.Color);
                            var animationCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherSandstormsAnimationHighlight.Color);

                            var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol), new GradientStop((float)0.25, animationCol), new GradientStop((float)0.75, baseCol), new GradientStop((float)1, animationCol));
                            var gradientMove = new MoveGradientDecorator(surface, 220, false);

                            SetLinearGradientEffect(animationGradient, gradientMove, layer, new Size(100, 100), runningEffects, _gradientEffects);

                            return true;
                        }
                        break;
                    case "Thunder":
                    case "Thunderstorms":
                        if (reactiveWeatherEffects && effectSettings.weather_thunder_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherThunderBase.Color);
                            var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.WeatherThunderAnimation.Color) };

                            var storms = new StrobeDecorator(layer, 10 * 1000, 100, true, animationCol, surface, false, baseCol);

                            layer.Brush = new SolidColorBrush(baseCol);
                            SetEffect(storms, layer, runningEffects);

                            return true;
                        }
                        break;
                    case "Umbral Wind":
                        if (reactiveWeatherEffects && effectSettings.weather_umbralwind_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherUmbralWindAnimationBase.Color);
                            var animationCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherUmbralWindAnimationHighlight.Color);

                            var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol), new GradientStop((float)0.25, animationCol), new GradientStop((float)0.75, baseCol), new GradientStop((float)1, animationCol));
                            var gradientMove = new MoveGradientDecorator(surface, 200, true);

                            SetLinearGradientEffect(animationGradient, gradientMove, layer, new Size(100, 100), runningEffects, _gradientEffects);

                            return true;
                        }
                        else
                        {
                            color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherUmbralWindBase.Color);
                        }
                        break;
                    case "Umbral Static":
                        if (reactiveWeatherEffects && effectSettings.weather_umbralstatic_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherUmbralStaticAnimationBase.Color);
                            var animationCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherUmbralStaticAnimationHighlight.Color);

                            var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol), new GradientStop((float)0.35, animationCol), new GradientStop((float)0.75, baseCol), new GradientStop((float)1, animationCol));
                            var gradientMove = new MoveGradientDecorator(surface, 100, true);

                            SetRadialGradientEffect(animationGradient, gradientMove, layer, new Size(100, 100), runningEffects, _gradientEffects);

                            return true;
                        }
                        else
                        {
                            color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherUmbralStaticBase.Color);
                        }
                        break;
                    case "Snow":
                        if (reactiveWeatherEffects && effectSettings.weather_snow_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherSnowBase.Color);
                            var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.WeatherSnowAnimationHighlight.Color) };
                            var starfield = new StarfieldDecorator(layer, (layer.Count() / 4), 40, 1500, animationCol, surface, false, baseCol);

                            layer.Brush = new SolidColorBrush(baseCol);
                            SetEffect(starfield, layer, runningEffects);

                            return true;
                        }
                        else
                        {
                            color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherSnowBase.Color);
                        }
                        break;
                    case "Blizzards":
                        if (reactiveWeatherEffects && effectSettings.weather_blizzard_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherBlizzardsBase.Color);
                            var animationCol = new Color[] { ColorHelper.ColorToRGBColor(_colorPalette.WeatherBilzzardsAnimationHighlight.Color) };
                            var starfield = new StarfieldDecorator(layer, (layer.Count() / 4), 20, 200, animationCol, surface, false, baseCol);

                            layer.Brush = new SolidColorBrush(baseCol);
                            SetEffect(starfield, layer, runningEffects);

                            return true;
                        }
                        else
                        {
                            color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherBlizzardsBase.Color);
                        }
                        break;
                    case "Everlasting Light":
                        if (reactiveWeatherEffects && effectSettings.weather_everlastinglight_animation)
                        {
                            var baseCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherEverlastingLightBase.Color);
                            var animationCol = ColorHelper.ColorToRGBColor(_colorPalette.WeatherEverlastingLightAnimationHighlight.Color);

                            var animationGradient = new LinearGradient(new GradientStop((float)0, baseCol), new GradientStop((float)0.25, animationCol), new GradientStop((float)0.50, baseCol), new GradientStop((float)0.75, animationCol), new GradientStop((float)1, baseCol));

                            var gradientMove = new MoveGradientDecorator(surface, 80, true);

                            SetLinearGradientEffect(animationGradient, gradientMove, layer, new Size(50, 50), runningEffects, _gradientEffects);

                            return true;
                        }
                        else
                        {
                            color = ColorHelper.ColorToRGBColor(_colorPalette.WeatherEverlastingLightBase.Color);
                        }
                        break;

                }

                //Apply Standard Lookup Weather

                StopEffects(layer, _gradientEffects);
                weather_brush.Color = color;
                layer.Brush = weather_brush;

                return false;
            }

            
        }

        public static Color GetWeatherColor(string weatherType, PaletteColorModel colorPalette)
        {
            var paletteType = typeof(PaletteColorModel);
            var fieldName = @"Weather" + weatherType.Replace(" ", "") + @"Base";
            var fieldInfo = paletteType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);

            if (fieldInfo == null)
            {
                #if DEBUG
                    Debug.WriteLine($"Unknown Weather Color Model: {fieldName}");
                #endif

                return ColorHelper.ColorToRGBColor(colorPalette.WeatherUnknownBase.Color);
            }

            var colorMapping = (ColorMapping)fieldInfo.GetValue(colorPalette);

            return ColorHelper.ColorToRGBColor(colorMapping.Color);
        }

    }

}
