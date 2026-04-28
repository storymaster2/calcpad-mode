namespace Calcpad.Server.Services.Storage
{
    public class S3Options
    {
        public bool Enabled { get; set; }
        public string ServiceURL { get; set; } = "";
        public string Region { get; set; } = "us-east-1";
        public string BucketName { get; set; } = "";
        public string AccessKey { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public bool ForcePathStyle { get; set; } = true;
        public bool UseHttps { get; set; }
        public bool EnsureBucketVersioning { get; set; } = true;
    }
}
