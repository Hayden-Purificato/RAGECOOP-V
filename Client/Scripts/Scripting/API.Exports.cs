﻿using Lidgren.Network;
using RageCoop.Core;
using RageCoop.Core.Scripting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using static RageCoop.Core.Scripting.CustomEvents;

namespace RageCoop.Client.Scripting
{
    internal static unsafe partial class API
    {
        static API()
        {
            RegisterFunctionPointers();
        }

        static void RegisterFunctionPointers()
        {
            foreach (var method in typeof(API).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attri = method.GetCustomAttribute<ApiExportAttribute>();
                if (attri == null) continue;
                attri.EntryPoint ??= method.Name;
                SHVDN.Core.SetPtr($"{typeof(API).FullName}.{attri.EntryPoint}", method.MethodHandle.GetFunctionPointer());
                Log.Debug($"Registered function pointer for {method.DeclaringType}.{method.Name}");
            }
        }

        [ThreadStatic]
        static string _lastResult;

        [ApiExportAttribute(EntryPoint = nameof(GetLastResult))]
        public static int GetLastResult(char* buf, int cbBufSize)
        {
            if (_lastResult == null)
                return 0;

            fixed (char* pErr = _lastResult)
            {
                var cbToCopy = sizeof(char) * (_lastResult.Length + 1);
                System.Buffer.MemoryCopy(pErr, buf, cbToCopy, Math.Min(cbToCopy, cbBufSize));
                if (cbToCopy > cbBufSize && cbBufSize > 0)
                {
                    buf[cbBufSize / sizeof(char) - 1] = '\0'; // Always add null terminator
                }
                return _lastResult.Length;
            }
        }
        public static void SetLastResult(string msg) => _lastResult = msg;

        [ApiExportAttribute(EntryPoint = nameof(SetLastResult))]
        public static void SetLastResult(char* msg)
        {
            try
            {
                SetLastResult(msg == null ? null : new string(msg));
            }
            catch (Exception ex)
            {
                SHVDN.PInvoke.MessageBoxA(default, ex.ToString(), "error", default);
            }
        }

        [ApiExportAttribute(EntryPoint = nameof(GetEventHash))]
        public static CustomEventHash GetEventHash(char* name) => new string(name);

        [ApiExportAttribute(EntryPoint = nameof(SendCustomEvent))]
        public static void SendCustomEvent(CustomEventFlags flags, int hash, byte* data, int cbData)
        {
            var payload = new byte[cbData];
            Marshal.Copy((IntPtr)data, payload, 0, cbData);
            Networking.Peer.SendTo(new Packets.CustomEvent()
            {
                Flags = flags,
                Payload = payload,
                Hash = hash
            }, Networking.ServerConnection, ConnectionChannel.Event, NetDeliveryMethod.ReliableOrdered);
        }

        [ApiExportAttribute(EntryPoint = nameof(InvokeCommand))]
        public static int InvokeCommand(char* name, int argc, char** argv)
        {
            try
            {
                var args = new string[argc];
                for (int i = 0; i < argc; i++)
                {
                    args[i] = new(argv[i]);
                }
                _lastResult = _invokeCommand(new string(name), args);
                return _lastResult.Length;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                SetLastResult(ex.ToString());
                return 0;
            }
        }

        [ApiExportAttribute(EntryPoint = nameof(GetLastResultLenInChars))]
        public static int GetLastResultLenInChars() => _lastResult?.Length ?? 0;

        /// <summary>
        ///     Convert Entity ID to handle
        /// </summary>
        [ApiExportAttribute(EntryPoint = nameof(IdToHandle))]
        public static int IdToHandle(byte type, int id)
        {
            return type switch
            {
                T_ID_PROP => EntityPool.GetPropByID(id)?.MainProp?.Handle ?? 0,
                T_ID_PED => EntityPool.GetPedByID(id)?.MainPed?.Handle ?? 0,
                T_ID_VEH => EntityPool.GetVehicleByID(id)?.MainVehicle?.Handle ?? 0,
                T_ID_BLIP => EntityPool.GetBlipByID(id)?.Handle ?? 0,
                _ => 0,
            };
        }

        /// <summary>
        /// Enqueue a message to the main logger
        /// </summary>
        /// <param name="level"></param>
        /// <param name="msg"></param>
        [ApiExportAttribute(EntryPoint = nameof(LogEnqueue))]
        public static void LogEnqueue(LogLevel level, char* msg)
        {
            Log.Enqueue((int)level, new(msg));
        }

        class ApiExportAttribute : Attribute
        {
            public string EntryPoint;
        }
    }
}