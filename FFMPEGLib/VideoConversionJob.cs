namespace FFMPEGLib
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class VideoConversionJob
    {
        public VideoConversionJob() { }

        public static VideoConversionJob FromJson(string json)
        {
            return JsonConvert.DeserializeObject<VideoConversionJob>(json);
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.None);
        }

        public List<string> InputArgs { get; set; }

        public List<string> ResultArgs { get; set; }

        public string LoggingBlob { get; set; }

        public List<string> FFMPEGCommandLines { get; set; }
        
        public string JobCompletionNotificationUrl { get; set; }
    }
}
