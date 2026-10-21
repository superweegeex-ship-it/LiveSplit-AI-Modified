using System;

namespace RetroSpy.Readers
{
    public static class Nintendo64
    {
        private const int PACKET_SIZE = 32;
        private const int NICOHOOD_PACKET_SIZE = 4;

        private static readonly string?[] BUTTONS = {
            "a", "b", "z", "start", "up", "down", "left", "right", null, null, "l", "r", "cup", "cdown", "cleft", "cright"
        };

        private static readonly string?[] NICOHOOD_BUTTONS = {
            "right", "left", "down", "up", "start", "z", "b", "a", "cright", "cleft", "cdown", "cup", "r", "l", null, null
        };

        private static float ReadStick(byte input)
        {
            return (float)(sbyte)input / 128;
        }

        public static ControllerStateEventArgs? ReadFromPacket(byte[]? packet)
        {
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            if (packet.Length == NICOHOOD_PACKET_SIZE) // Packets are written as bytes when writing from the NicoHood API, so we're looking for a packet size of 4 (interpreted as bytes)
            {

                ControllerStateBuilder stateNico = new();

                for (int i = 0; i < 16; i++) // Handles the two button bytes
                {
                    if (string.IsNullOrEmpty(NICOHOOD_BUTTONS[i])) continue;
                    int bitPacket = (packet[i / 8] >> (i % 8)) & 0x1;
                    stateNico.SetButton(NICOHOOD_BUTTONS[i], bitPacket != 0x00);
                }

                stateNico.SetAnalog("stick_x", ReadStick(packet[2]), packet[2]);
                stateNico.SetAnalog("stick_y", ReadStick(packet[3]), packet[3]);

                return stateNico.Build();
            }

            if (packet.Length != PACKET_SIZE)
            {
                return null;
            }

            ControllerStateBuilder state = new();

            for (int i = 0; i < BUTTONS.Length; ++i)
            {
                if (string.IsNullOrEmpty(BUTTONS[i]))
                {
                    continue;
                }

                state.SetButton(BUTTONS[i], packet[i] != 0x00);
            }

            float x = ReadStick(SignalTool.ReadByte(packet, BUTTONS.Length));
            float y = ReadStick(SignalTool.ReadByte(packet, BUTTONS.Length + 8));
            state.SetAnalog("stick_x", x, SignalTool.ReadByte(packet, BUTTONS.Length));
            state.SetAnalog("stick_y", y, SignalTool.ReadByte(packet, BUTTONS.Length + 8));

            SignalTool.SetMouseProperties(x, y, SignalTool.ReadByte(packet, BUTTONS.Length), SignalTool.ReadByte(packet, BUTTONS.Length + 8), state);

            return state.Build();
        }
    }
}
