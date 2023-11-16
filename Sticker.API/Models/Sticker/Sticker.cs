using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Sticker.API.ReusableClass;

namespace Sticker.API.Models.Sticker
{
    public class ReplyInfo 
    {
        public ReplyInfo(int UUID, bool isAnonymous)
        {
            this.UUID = UUID;
            IsAnonymous = isAnonymous;
        }

        public int UUID { get; set; }
        public bool IsAnonymous { get; set; }
    }
    public class Sticker
    {
        public Sticker(string? id, int UUID, bool isAnonymous, bool isDeleted, DateTime createdTime, List<string> timeLine, ReplyInfo? replyTo, string text, List<string> tags, List<MediaMetadata> medias)
        {
            Id = id;
            this.UUID = UUID;
            IsAnonymous = isAnonymous;
            IsDeleted = isDeleted;
            CreatedTime = createdTime;
            TimeLine = timeLine;
            ReplyTo = replyTo;
            Text = text;
            Tags = tags;
            Medias = medias;
        }

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public int UUID { get; set; }
        public bool IsAnonymous { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime CreatedTime { get; set; }
        public List<string> TimeLine { get; set; }
        public ReplyInfo? ReplyTo { get; set; }
        public string Text { get; set; }
        public List<string> Tags { get; set; }
        public List<MediaMetadata> Medias { get; set; }
    }
}
