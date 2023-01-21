﻿using Chromatics.Extensions.RGB.NET.Decorators;
using Chromatics.Helpers;
using Chromatics.Layers;
using Chromatics.Models;
using RGB.NET.Core;
using RGB.NET.Devices.Asus;
using RGB.NET.Devices.CoolerMaster;
using RGB.NET.Devices.Corsair;
using RGB.NET.Devices.Logitech;
using RGB.NET.Devices.Msi;
using RGB.NET.Devices.Novation;
using RGB.NET.Devices.Razer;
using RGB.NET.Devices.SteelSeries;
using RGB.NET.Devices.Wooting;
using RGB.NET.Presets.Decorators;
using RGB.NET.Presets.Groups;
using RGB.NET.Presets.Textures;
using RGB.NET.Presets.Textures.Gradients;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chromatics.Core
{
    public static class RGBController
    {
        private static RGBSurface surface = new RGBSurface();

        private static bool _loaded;

        private static bool _wasPreviewed;

        private static List<IRGBDevice> _devices = new List<IRGBDevice>();

        private static Dictionary<int, ListLedGroup> _layergroups = new Dictionary<int, ListLedGroup>();

        private static List<Led> _layergroupledcollection = new List<Led>();

        private static PaletteColorModel _colorPalette = new PaletteColorModel();

        private static EffectTypesModel _effects = new EffectTypesModel();

        private static List<ListLedGroup> _runningEffects = new List<ListLedGroup>();

        public static void Setup()
        {
            //Bind to console
            Logger.WriteConsole(Enums.LoggerTypes.Devices, @"Looking for RGB Devices..");

            surface.Exception += args_ => throw args_.Exception;//Logger.WriteConsole(Enums.LoggerTypes.Devices, $"Device Error: {args_.Exception.Message}");

            //Load devices
            //surface.Load(LogitechDeviceProvider.Instance);
            surface.Load(CorsairDeviceProvider.Instance, RGBDeviceType.All);
            //surface.Load(CoolerMasterDeviceProvider.Instance);
            //surface.Load(NovationDeviceProvider.Instance);
            surface.Load(RazerDeviceProvider.Instance, RGBDeviceType.All);
            //surface.Load(AsusDeviceProvider.Instance);
            //surface.Load(MsiDeviceProvider.Instance);
            //surface.Load(SteelSeriesDeviceProvider.Instance);
            //surface.Load(WootingDeviceProvider.Instance);


            var deviceCount = 0;
            foreach (var surfaceDevice in surface.Devices)
            {
                Logger.WriteConsole(Enums.LoggerTypes.Devices, $"Found {surfaceDevice.DeviceInfo.Manufacturer} {surfaceDevice.DeviceInfo.DeviceType}: {surfaceDevice.DeviceInfo.DeviceName}.");
                _devices.Add(surfaceDevice);
                deviceCount++;

            }
                

            var TimerTrigger = new TimerUpdateTrigger();
            TimerTrigger.UpdateFrequency = 0.05;
            surface.RegisterUpdateTrigger(TimerTrigger);

            surface.AlignDevices();
            surface.Updating += Surface_Updating;

            //Startup Effects
            RunStartupEffects();

                        
            Logger.WriteConsole(Enums.LoggerTypes.Devices, $"{deviceCount} devices loaded.");
            _loaded = true;
        }
                

        public static void Unload()
        {
            if (!_loaded) return;

            try
            {
                surface.Updating -= Surface_Updating;
                surface?.Dispose(); 
            } catch { }
        }

        public static bool IsLoaded()
        {
            return _loaded;
        }

        public static PaletteColorModel GetActivePalette()
        {
            return _colorPalette;
        }

        public static bool LoadColorPalette()
        {
            if (FileOperationsHelper.CheckColorMappingsExist())
            {
                _colorPalette = FileOperationsHelper.LoadColorMappings();

                return true;
            }

            return false;
        }

        public static bool SaveColorPalette()
        {
            FileOperationsHelper.SaveColorMappings(_colorPalette);
            return true;
        }

        public static bool ImportColorPalette()
        {
            var colorPalette = FileOperationsHelper.ImportColorMappings();

            if (colorPalette != null)
            {
                _colorPalette = colorPalette;

                SaveColorPalette();
                return true;
            }

            return false;
        }

        public static bool ExportColorPalette()
        {
            FileOperationsHelper.ExportColorMappings(_colorPalette);
            return true;
        }

        public static EffectTypesModel GetEffectsSettings()
        {
            return _effects;
        }

        public static bool LoadEffectsSettings()
        {
            if (FileOperationsHelper.CheckEffectSettingsExist())
            {
                _effects = FileOperationsHelper.LoadEffectSettings();

                return true;
            }

            return false;
        }

        public static bool SaveEffectsSettings()
        {
            FileOperationsHelper.SaveEffectSettings(_effects);
            return true;
        }

        private static void RunStartupEffects()
        {
            var devices = surface.GetDevices(RGBDeviceType.All);

            var move = new MoveGradientDecorator(surface)
            {
                IsEnabled = true,
                Speed = 100,
            };

            //Add base black layer
            
            //var background = new ListLedGroup(surface, surface.Leds);
            //background.Brush = new SolidColorBrush(new Color(0, 0, 0));

            foreach (var device in devices)
            {
                var gradient = new RainbowGradient();
                var ledgroup = new ListLedGroup(surface);

                ledgroup.ZIndex = 1;
                foreach (var led in device)
                {
                    ledgroup.AddLed(led);
                }

                gradient.AddDecorator(move);

                if (device.DeviceInfo.DeviceType == RGBDeviceType.Keyboard)
                {
                    ledgroup.Brush = new TextureBrush(new ConicalGradientTexture(new Size(100, 100), gradient));
                }
                else
                {
                    ledgroup.Brush = new TextureBrush(new LinearGradientTexture(new Size(100, 100), gradient));
                }
                    

                _runningEffects.Add(ledgroup);
            }
        }

        private static void StopEffects()
        {
            foreach (var effects in _runningEffects)
            {
                foreach (var decorator in effects.Decorators)
                {
                    decorator.IsEnabled = false;
                }
                                        
                effects.RemoveAllDecorators();
                effects.Detach();
            }

            _runningEffects.Clear();
        }

        private static void ResetLayerGroups()
        {
            foreach (var layergroup in _layergroups)
            {
                surface.Detach(layergroup.Value);
            }

            _layergroups.Clear();
            _layergroupledcollection.Clear();
        }

        private static void Surface_Updating(UpdatingEventArgs args)
        {
            if (!_loaded) return;

            //If Previewing layers - update RGB on each refresh cycle
            if (MappingLayers.IsPreview())
            {
                if (!_wasPreviewed)
                {
                    ResetLayerGroups();
                    _wasPreviewed = true;
                }

                var layers = MappingLayers.GetLayers().OrderBy(x => x.Value.zindex);

                //Release any running effects
                StopEffects();

                //Display mappings on devices
                foreach (var layer in layers)
                {
                    var mapping = layer.Value;

                    if (mapping.rootLayerType == Enums.LayerType.EffectLayer) continue;

                    //Loop through all LED's and assign to device layer
                    var devices = surface.GetDevices(mapping.deviceType);

                    var layergroup = new ListLedGroup(surface)
                    {
                        ZIndex = mapping.zindex,
                    };

                    if (_layergroups.ContainsKey(mapping.layerID))
                    {
                        layergroup = _layergroups[mapping.layerID];
                    }
                    else
                    {
                        _layergroups.Add(mapping.layerID, layergroup);
                    }


                    var drawing_col = (System.Drawing.Color)EnumExtensions.GetAttribute<DefaultValueAttribute>(mapping.rootLayerType).Value;
                    var highlight_col = ColorHelper.ColorToRGBColor(drawing_col);

                    foreach (var device in devices)
                    {
                        if (!_devices.Contains(device)) continue;

                        foreach (var led in device)
                        {
                            if (!mapping.deviceLeds.Any(v => v.Value.Equals(led.Id)))
                            {
                                layergroup.RemoveLed(led);
                                _layergroupledcollection.Remove(led);
                                continue;
                            }

                            if (!mapping.Enabled && mapping.rootLayerType == Enums.LayerType.BaseLayer)
                            {
                                drawing_col = System.Drawing.Color.Black;
                                highlight_col = ColorHelper.ColorToRGBColor(drawing_col);
                            }
                            else if (!mapping.Enabled)
                            {
                                if (_layergroupledcollection.Contains(led))
                                {
                                    layergroup.RemoveLed(led);
                                    _layergroupledcollection.Remove(led);
                                }

                                continue;
                            }

                            if (led.Color != highlight_col)
                            {
                                layergroup.RemoveLed(led);
                                led.Color = highlight_col;
                            }

                            if (!_layergroupledcollection.Contains(led))
                            {
                                _layergroupledcollection.Add(led);
                            }

                            layergroup.AddLed(led);

                        }

                    }

                    //Apply lighting
                    var brush = new SolidColorBrush(highlight_col);

                    layergroup.Brush = brush;
                    //Debug.WriteLine($"Layer {mapping.layerID} at zindex {mapping.zindex} to {highlight_col}");
                }
            }
            else
            {
                if (_wasPreviewed)
                {            
                    ResetLayerGroups();
                    
                    RunStartupEffects();
                    _wasPreviewed = false;
                    
                }
            }
        }
    }
}