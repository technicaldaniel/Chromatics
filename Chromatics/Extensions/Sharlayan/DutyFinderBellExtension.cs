using Microsoft.VisualBasic;
using Sharlayan;
using Sharlayan.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Chromatics.Extensions.Sharlayan
{
    public class DutyFinderBellExtension
    {
        public static DateTime LastUpdated = DateTime.MinValue;

        private static readonly string memoryName = @"DUTYFINDER";
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.05);
        private static bool _siginit;
        private static bool _memoryready;
        private static bool _isPopped;
        private static bool _inInstance;
        private static byte _instanceState;
        private static bool _initialized;
        private static List<Signature> _sList;
        private static readonly object RefreshLock = new object();
        private static readonly object CacheLock = new object();
        private static MemoryHandler _memoryHandler;

        public static void RefreshData(MemoryHandler memoryHandler)
        {
            lock (RefreshLock)
            {
                _memoryHandler = memoryHandler;

                if (!_memoryready)
                {
                    if (!_memoryHandler.Scanner.Locations.ContainsKey(memoryName) || !_siginit)
                    {

                        _sList = new List<Signature>
                        {
                            new Signature
                            {
                                Key = memoryName,
                                Value = "440fb643**488d51**488d0d",
                                ASMSignature = true,
                                PointerPath = new List<long>
                                {
                                    0,
                                    0
                                }
                            }
                        };

                        _memoryHandler.Scanner.LoadOffsets(_sList.ToArray());

                        Thread.Sleep(100);

                        if (_memoryHandler.Scanner.Locations.ContainsKey(memoryName))
                        {
                            #if DEBUG
                                Debug.WriteLine($"Found {memoryName}. Location: {_memoryHandler.Scanner.Locations[memoryName].GetAddress().ToInt64():X}");
                            #endif

                            _siginit = true;
                        }

                        if (_siginit)
                            _memoryready = true;
                    }
                }
                else
                {
                    if (_memoryHandler.Scanner.Locations.ContainsKey(memoryName))
                    {
                        var address = _memoryHandler.Scanner.Locations[memoryName];
                        var contentFinderState = _memoryHandler.GetByte(address.GetAddress(), 0x14D);

                        _isPopped = contentFinderState == 3; //ContentFinderState of 3 means DF pop but not entered yet
                        _inInstance = contentFinderState > 3;
                        _instanceState = contentFinderState;

                        _initialized = true;
                    }


                    LastUpdated = DateTime.Now;
                }
            }
        }

        public static void CheckCache()
        {
            lock (CacheLock)
            {
                if (LastUpdated + UpdateInterval <= DateTime.Now)
                    RefreshData(_memoryHandler);
            }
        }

        public static bool IsPopped()
        {
            if (!_initialized)
                return false;

            CheckCache();

            return _isPopped;
        }

        public static bool InInstance()
        {
            if (!_initialized)
                return false;

            CheckCache();

            return _inInstance;
        }

        public static byte instanceState()
        {
            if (!_initialized)
                return 0;

            CheckCache();

            return _instanceState;
        }
    }
}
