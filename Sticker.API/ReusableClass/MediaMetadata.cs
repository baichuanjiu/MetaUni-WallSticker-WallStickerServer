namespace Sticker.API.ReusableClass
{
    public class MediaMetadata
    {
        public MediaMetadata(string type, string URL, double aspectRatio, string? previewImage, int? timeTotal)
        {
            Type = type;
            this.URL = URL;
            AspectRatio = aspectRatio;
            PreviewImage = previewImage;
            TimeTotal = timeTotal;
        }

        public string Type { get; set; }
        public string URL { get; set; }
        public double AspectRatio { get; set; }
        public string? PreviewImage { get; set; }
        //TimeTotal: milliseconds
        public int? TimeTotal { get; set; }
    }
}
