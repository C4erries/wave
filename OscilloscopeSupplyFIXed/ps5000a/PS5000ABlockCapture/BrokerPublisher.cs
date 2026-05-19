using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PS5000A
{
    internal static class BrokerPublisher
    {
        public static string BrokerUrl { get; set; } = "http://localhost:8090";
        public static string TopicName { get; set; } = "raw.osc.chA";
        public static bool Enabled { get; set; } = false;

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        public static byte[] EncodeFrame(
            long timestampNs, int sampleRateHz, int nSamples,
            byte channelId, byte sourceId, float[] samples)
        {
            byte[] buf = new byte[20 + nSamples * 4];

            WriteInt64BE(buf, 0, timestampNs);
            WriteInt32BE(buf, 8, sampleRateHz);
            WriteInt32BE(buf, 12, nSamples);
            buf[16] = channelId;
            buf[17] = sourceId;
            WriteInt16BE(buf, 18, 0);

            for (int i = 0; i < nSamples; i++)
            {
                byte[] fb = BitConverter.GetBytes(samples[i]);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(fb);
                Buffer.BlockCopy(fb, 0, buf, 20 + i * 4, 4);
            }

            return buf;
        }

        public static void PublishAsync(byte[] frame)
        {
            if (!Enabled) return;
            Task.Run(async () =>
            {
                try
                {
                    string b64 = "base64:" + Convert.ToBase64String(frame);
                    string json = "{\"value\":\"" + b64 + "\",\"contentType\":\"application/octet-stream\"}";
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    string url = BrokerUrl + "/api/topics/" + TopicName + "/partitions/0/messages";
                    var resp = await _http.PostAsync(url, content);
                    if (!resp.IsSuccessStatusCode)
                        Debug.WriteLine("[BrokerPublisher] HTTP " + (int)resp.StatusCode);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[BrokerPublisher] " + ex.Message);
                }
            });
        }

        public static async Task<bool> CheckTopicExistsAsync()
        {
            try
            {
                var resp = await _http.GetAsync(BrokerUrl + "/api/topics/" + TopicName);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private static void WriteInt64BE(byte[] buf, int offset, long value)
        {
            byte[] b = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value));
            Buffer.BlockCopy(b, 0, buf, offset, 8);
        }

        private static void WriteInt32BE(byte[] buf, int offset, int value)
        {
            byte[] b = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value));
            Buffer.BlockCopy(b, 0, buf, offset, 4);
        }

        private static void WriteInt16BE(byte[] buf, int offset, short value)
        {
            byte[] b = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value));
            Buffer.BlockCopy(b, 0, buf, offset, 2);
        }
    }
}
