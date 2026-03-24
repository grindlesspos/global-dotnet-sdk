using System;
using System.Drawing;
using System.Collections.Generic;
using System.Text;
using GlobalPayments.Api.Terminals.Abstractions;
using GlobalPayments.Api.Terminals.HPA;
using System.IO;
using System.Drawing.Imaging;
using GlobalPayments.Api.Terminals.UPA;
using GlobalPayments.Api.Utils;

namespace GlobalPayments.Api.Terminals {
    public class TerminalUtilities {
        const string _version = "1.35";

        private static string GetElementString(object[] elements) {
            var sb = new StringBuilder();
            foreach (var element in elements) {
                if (element is ControlCodes)
                    sb.Append((char)((ControlCodes)element));
                else if (element is IRequestSubGroup)
                    sb.Append(((IRequestSubGroup)element).GetElementString());
                else if (element is string[])
                    foreach (var sub_element in element as string[]) {
                        sb.Append((char)ControlCodes.FS);
                        sb.Append(sub_element);
                    }
                else sb.Append(element);
            }

            return sb.ToString();
        }

        private static DeviceMessage BuildMessage(string messageId, string message) {
            var buffer = new List<byte>();

            // Begin Message
            buffer.Add((byte)ControlCodes.STX);
            
            // Add Message ID
            foreach (char c in messageId)
                buffer.Add((byte)c);
            buffer.Add((byte)ControlCodes.FS);

            // Add Version
            foreach (char c in _version)
                buffer.Add((byte)c);
            buffer.Add((byte)ControlCodes.FS);

            // Add the Message
            if (!string.IsNullOrEmpty(message)) {
                foreach (char c in message)
                    buffer.Add((byte)c);
            }

            // End the Message
            buffer.Add((byte)ControlCodes.ETX);

            byte lrc = CalculateLRC(buffer.ToArray());
            buffer.Add(lrc);

            return new DeviceMessage(buffer.ToArray());
        }

        public static DeviceMessage BuildRequest(string message, MessageFormat format) {
            var buffer = new List<byte>();

            if (format == MessageFormat.Visa2nd)
                buffer.Add((byte)ControlCodes.STX);
            else {
                var length_bytes = BitConverter.GetBytes(message.Length);
                buffer.Add(length_bytes[1]);
                buffer.Add(length_bytes[0]);
            }

            foreach (char c in message)
                buffer.Add((byte)c);

            if (format == MessageFormat.Visa2nd) {
                // End the Message
                buffer.Add((byte)ControlCodes.ETX);

                byte lrc = CalculateLRC(buffer.ToArray());
                buffer.Add(lrc);
            }

            return new DeviceMessage(buffer.ToArray());
        }

        public static DeviceMessage BuildUpaAdminRequest(int requestId, string ecrId, string txnType, string lineItemLeft = null, string lineItemRight = null, int? displayOption = null)
        {
            var doc = new JsonDoc();
            doc.Set("message", UpaMessageType.Msg);
            var data = doc.SubElement("data");
            data.Set("command", txnType);
            data.Set("EcrId", ecrId);
            data.Set("requestId", requestId);

            if ((!string.IsNullOrEmpty(lineItemLeft) || !string.IsNullOrEmpty(lineItemRight)) || displayOption.HasValue) {
                var request = data.SubElement("data");
                var requestParams = request.SubElement("params");
                requestParams.Set("lineItemLeft", lineItemLeft);
                requestParams.Set("lineItemRight", lineItemRight);
                if (displayOption.HasValue) {
                    requestParams.Set("displayOption", displayOption);
                }
            }

            return BuildUpaRequest(doc.ToString());
        }

        public static DeviceMessage BuildRequest(string messageId, params object[] elements) {
            var message = GetElementString(elements);
            return BuildMessage(messageId, message);
        }

        public static DeviceMessage BuildUpaRequest(string jsonRequest)
        {
            jsonRequest = jsonRequest.Replace("ecrId", "EcrId");

            jsonRequest = jsonRequest.Replace("<LF>", "\r\n");
            jsonRequest = jsonRequest.Replace("{", "{\r\n");
            jsonRequest = jsonRequest.Replace("}", "}\r\n");
            jsonRequest = jsonRequest.Replace(",", ",\r\n");
            var buffer = new List<byte>();

            // Begin Message
            buffer.Add((byte)ControlCodes.STX);
            buffer.Add(0x0A);

            // Add the Message
            if (!string.IsNullOrEmpty(jsonRequest))
            {
                foreach (char c in jsonRequest)
                    buffer.Add((byte)c);
            }

            // End the Message
            buffer.Add((byte)ControlCodes.LF);
            buffer.Add((byte)ControlCodes.ETX);
            buffer.Add((byte)ControlCodes.LF);
            return new DeviceMessage(buffer.ToArray());
        }

        public static byte CalculateLRC(byte[] buffer) {
            // account for LRC still being attached
            var length = buffer.Length;
            if (buffer[buffer.Length - 1] != (byte)ControlCodes.ETX)
                length--;

            byte lrc = new byte();
            for (int i = 1; i < length; i++)
                lrc = (byte)(lrc ^ buffer[i]);
            return lrc;
        }

        public static byte[] BuildSignatureImage(string pathData, int width = 150, int height = 100) {

            int yOffset = 0;

            Func<string, Point> toPoint = (coord) => {
                var xy = coord.Split(',');
                return new Point {
                    X = int.Parse(xy[0]),
                    Y = int.Parse(xy[1]) + yOffset
                };
            };

            // parse instructions
            var coordinates = pathData.Split('^');

            if (height > 100)
            {
                // image may include a lot of dead space, let's trim it off. Square signatures are uncommon...
                int minY = height, maxY = 0;
                foreach( var coord in coordinates)
                {
                    if (coord == "~") break;
                    if (coord == "0,65535") continue;

                    var pt = toPoint(coord);
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }

                //
                // set our offset and new height.
                //
                if (minY > maxY)  // empty image
                    height = 100;
                else if (maxY - minY < 100)
                {
                    // keep minimum height of 100 for consistency.
                    height = 100;
                    // equally space signature within the 100 pixel height
                    yOffset = 50 - (maxY - minY + 1) / 2 - minY;
                }
                else
                {
                    yOffset = -minY;
                    height = maxY - minY + 1;
                }
            }


            Bitmap bmp = new Bitmap(width, height);
            Pen pen = Pens.Black;
            if (width >= 200) pen = new Pen(Color.Black, width / 100.0f);

            var gfx = Graphics.FromImage(bmp);
            gfx.Clear(Color.White);

            var index = 0;

            var coordinate = coordinates[index++];
            do {
                if(coordinate == "0,65535")
                    coordinate = coordinates[index++];
                //
                // A35 experience: point data can have multiple 0,65535 in a row,
                // including an EVEN number in a row. Thus, we need to check
                // our start coordinate is valid and if not, 
                //
                if (coordinate != "0,65535")
                {
                    var start = toPoint(coordinate);

                    coordinate = coordinates[index++];
                    if (coordinate == "0,65535")
                        gfx.FillRectangle(Brushes.Black, start.X, start.Y, 1, 1);
                    else
                    {
                        var end = toPoint(coordinate);
                        gfx.DrawLine(pen, start, end);
                    }
                }
            }
            while (coordinates[index] != "~");

            using (var ms = new MemoryStream()) {
                bmp.Save(ms, ImageFormat.Bmp);
                return ms.ToArray();
            }
        }
    }
}
