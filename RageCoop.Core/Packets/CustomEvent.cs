﻿using Lidgren.Network;
using RageCoop.Core.Scripting;
using System;
namespace RageCoop.Core
{
    internal partial class Packets
    {

        internal class CustomEvent : Packet
        {
            public static Func<byte, NetIncomingMessage, object> ResolveHandle = null;
            public CustomEventFlags Flags;
            public override PacketType Type => PacketType.CustomEvent;
            public CustomEvent(CustomEventFlags flags = CustomEventFlags.None)
            {
                Flags = flags;
            }
            public int Hash { get; set; }
            public object[] Args { get; set; }

            protected override void Serialize(NetOutgoingMessage m)
            {
                Args = Args ?? new object[] { };
                m.Write((byte)Flags);
                m.Write(Hash);
                m.Write(Args.Length);
                foreach (var arg in Args)
                {
                    CoreUtils.GetBytesFromObject(arg, m);
                }
            }

            public override void Deserialize(NetIncomingMessage m)
            {

                Flags = (CustomEventFlags)m.ReadByte();
                Hash = m.ReadInt32();
                var len = m.ReadInt32();
                Args = new object[len];
                for (int i = 0; i < len; i++)
                {
                    byte type = m.ReadByte();
                    switch (type)
                    {
                        case 0x01:
                            Args[i] = m.ReadByte(); break;
                        case 0x02:
                            Args[i] = m.ReadInt32(); break;
                        case 0x03:
                            Args[i] = m.ReadUInt16(); break;
                        case 0x04:
                            Args[i] = m.ReadInt32(); break;
                        case 0x05:
                            Args[i] = m.ReadUInt32(); break;
                        case 0x06:
                            Args[i] = m.ReadInt64(); break;
                        case 0x07:
                            Args[i] = m.ReadUInt64(); break;
                        case 0x08:
                            Args[i] = m.ReadFloat(); break;
                        case 0x09:
                            Args[i] = m.ReadBoolean(); break;
                        case 0x10:
                            Args[i] = m.ReadString(); break;
                        case 0x11:
                            Args[i] = m.ReadVector3(); break;
                        case 0x12:
                            Args[i] = m.ReadQuaternion(); break;
                        case 0x13:
                            Args[i] = (GTA.Model)m.ReadInt32(); break;
                        case 0x14:
                            Args[i] = m.ReadVector2(); break;
                        case 0x15:
                            Args[i] = m.ReadByteArray(); break;
                        default:
                            if (ResolveHandle == null)
                            {
                                throw new InvalidOperationException($"Unexpected type: {type}");
                            }
                            else
                            {
                                Args[i] = ResolveHandle(type, m); break;
                            }
                    }
                }
            }
        }
    }
}
