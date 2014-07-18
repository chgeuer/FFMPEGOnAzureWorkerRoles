namespace FFMPEGLib
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class VideoConversionJobResults
    {
        public VideoConversionJobResults()
        {
            Results = new List<VideoConversionJobResult>();
        }

        public static VideoConversionJobResults FromJson(string json)
        {
            return JsonConvert.DeserializeObject<VideoConversionJobResults>(json);
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.None);
        }

        public List<VideoConversionJobResult> Results { get; set; }

        public string LoggerData { get; set; }
    }

    public class VideoConversionJobResult
    {
        public string CommandLine { get; set; }

        public string StandardOut { get; set; }

        public string StandardErr { get; set; }

        public int ErrorCode { get; set; }
    }
}
