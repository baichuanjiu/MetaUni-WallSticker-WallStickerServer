using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Sticker.API.DataCollection.Sticker;

namespace Sticker.API.MongoDBServices.Sticker
{
    public class StickerService
    {
        private readonly IMongoCollection<Models.Sticker.Sticker> _stickerCollection;
        private readonly IMongoCollection<BsonDocument> _bsonDocumentsCollection;

        public StickerService(IOptions<StickerCollectionSettings> stickerCollectionSettings)
        {
            var mongoClient = new MongoClient(
                stickerCollectionSettings.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                stickerCollectionSettings.Value.DatabaseName);

            _stickerCollection = mongoDatabase.GetCollection<Models.Sticker.Sticker>(
                stickerCollectionSettings.Value.StickerCollectionName);

            _bsonDocumentsCollection = mongoDatabase.GetCollection<BsonDocument>(
                stickerCollectionSettings.Value.StickerCollectionName);
        }

        public async Task<bool> CheckStatusAsync(string id)
        {
            // return true 表示存在且未被删除，可操作
            // return false 表示不存在或已被删除，不可操作
            return (await _stickerCollection.Find(sticker => sticker.Id == id && !sticker.IsDeleted).FirstOrDefaultAsync()) != null;
        }

        public async Task<Models.Sticker.Sticker?> GetStickerByIdAsync(string id)
        {
            return await _stickerCollection.Find(sticker => sticker.Id == id).FirstOrDefaultAsync();
        }

        public async Task<List<string>?> GetTimeLineAsync(string id)
        {
            Models.Sticker.Sticker? sticker = await GetStickerByIdAsync(id);
            if (sticker != null)
            {
                return sticker.TimeLine;
            }
            else 
            {
                return null;
            }
        }

        public async Task<Models.Sticker.ReplyInfo?> GetReplyInfoAsync(string id)
        {
            Models.Sticker.Sticker? sticker = await _stickerCollection.Find(sticker => sticker.Id == id).FirstOrDefaultAsync();
            if (sticker != null)
            {
                return new Models.Sticker.ReplyInfo(sticker.UUID,sticker.IsAnonymous);
            }
            else
            {
                return null;
            }
        }

        public async Task CreateAsync(Models.Sticker.Sticker sticker)
        {
            await _stickerCollection.InsertOneAsync(sticker);
        }

        public async Task DeleteAsync(string id) 
        {
            var update = Builders<Models.Sticker.Sticker>.Update
                .Set(sticker => sticker.Text,"该贴贴已被撕下")
                .Set(sticker => sticker.Tags,new())
                .Set(sticker => sticker.Medias,new())
                .Set(sticker => sticker.IsDeleted,true);

            await _stickerCollection.UpdateOneAsync(sticker => sticker.Id == id,update);
        }

        public async Task<List<Models.Sticker.Sticker>?> GetTimeLineStickersByOffsetAsync(string id, int offset) 
        {
            List<string>? timeLine = await GetTimeLineAsync(id);
            if (timeLine == null) 
            {
                return null;
            }
            if (timeLine.Count == 0)
            {
                return new();
            }

            return await _stickerCollection
                .Find(sticker => timeLine.Contains(sticker.Id!))
                .SortByDescending(sticker => sticker.CreatedTime)
                .Skip(offset)
                .Limit(20)
                .ToListAsync();
        }

        public async Task<List<Models.Sticker.Sticker>?> GetTimeLineStickersWhenReplyingByOffsetAsync(string id, int offset)
        {
            List<string>? timeLine = await GetTimeLineAsync(id);
            if (timeLine == null)
            {
                return null;
            }
            timeLine.Add(id);

            return await _stickerCollection
                .Find(sticker => timeLine.Contains(sticker.Id!))
                .SortByDescending(sticker => sticker.CreatedTime)
                .Skip(offset)
                .Limit(20)
                .ToListAsync();
        }

        public async Task<List<Models.Sticker.Sticker>> GetRepliesAsync(List<string> replies,int offset)
        {
            return await _stickerCollection
                .Find(sticker => replies.Contains(sticker.Id!))
                .SortBy(sticker => sticker.CreatedTime)
                .Skip(offset)
                .Limit(20)
                .ToListAsync();
        }

        public async Task<List<Models.Sticker.Sticker>> GetStickersByIdListAsync(List<string> idList)
        {
            var stickers = await _stickerCollection
                .Find(sticker => idList.Contains(sticker.Id!))
                .ToListAsync() ;
            stickers.Sort((a, b) => idList.IndexOf(a.Id!).CompareTo(idList.IndexOf(b.Id!)));
            return stickers ;
        }

        public async Task<long> GetMyStickersNumber(int uuid) 
        {
            return await _stickerCollection
                .Find(sticker => sticker.UUID == uuid && !sticker.IsDeleted)
                .CountDocumentsAsync();
        }

        public async Task<List<Models.Sticker.Sticker>> GetMyStickersByLastResultAsync(int uuid,DateTime? lastDateTime, string? lastId)
        {
            //MongoDB涉及到DateTime需要一律使用UTC进行操作（因为MongoDB默认采用UTC存储时间）
            if (lastDateTime == null)
            {
                lastDateTime = DateTime.UtcNow;
            }

            return await _stickerCollection
                .Find(sticker => sticker.UUID == uuid && sticker.CreatedTime.CompareTo(lastDateTime) <= 0 && !sticker.IsDeleted && sticker.Id != lastId)
                .SortByDescending(sticker => sticker.CreatedTime)
                .Limit(20)
                .ToListAsync();
        }

        public async Task<List<Models.Sticker.Sticker>> GetTodayStickersByLastResultAsync(DateTime? lastDateTime,string? lastId) 
        {
            //MongoDB涉及到DateTime需要一律使用UTC进行操作（因为MongoDB默认采用UTC存储时间）
            if (lastDateTime == null) 
            {
                lastDateTime = DateTime.Today.ToUniversalTime().AddDays(1);
            }
            DateTime now = DateTime.UtcNow;

            return await _stickerCollection
                .Find(sticker => sticker.CreatedTime.Date.Equals(now.Date) && sticker.CreatedTime.CompareTo(lastDateTime) <= 0 && !sticker.IsDeleted && sticker.Id != lastId)
                .SortByDescending(sticker => sticker.CreatedTime)
                .Limit(20)
                .ToListAsync();
        }

        // 首先规定一点：传进来的searchKeys不能为空数组
        // 要不要为查找做一层缓存？
        public async Task<List<Models.Sticker.Sticker>> Search(List<string> searchKeys,int offset)
        {
            var builder = Builders<Models.Sticker.Sticker>.Filter;

            FilterDefinition<Models.Sticker.Sticker> notDeletedFilter = builder.Where(sticker => !sticker.IsDeleted);

            FilterDefinition<Models.Sticker.Sticker> keyFilter;
            string firstKey = searchKeys[0];
            if (firstKey.StartsWith("#"))
            {
                keyFilter = builder.Where(sticker => sticker.Tags.Contains(firstKey.Remove(0, 1)));
            }
            else
            {
                keyFilter = builder.Regex(sticker => sticker.Text, firstKey);
            }
            searchKeys.RemoveAt(0);
            foreach (var key in searchKeys)
            {
                if (key.StartsWith("#"))
                {
                    keyFilter |= builder.Where(sticker => sticker.Tags.Contains(key.Remove(0, 1)));
                }
                else
                {
                    keyFilter |= builder.Regex(sticker => sticker.Text, key);
                }
            }

            return await _stickerCollection
                .Find(notDeletedFilter & keyFilter)
                .Skip(offset)
                .Limit(20)
                .ToListAsync();
        }
        public async Task<List<Models.Sticker.Sticker>> Search(List<string> searchKeys, int uuid,int offset)
        {
            var builder = Builders<Models.Sticker.Sticker>.Filter;

            FilterDefinition<Models.Sticker.Sticker> notDeletedFilter = builder.Where(sticker => !sticker.IsDeleted);

            FilterDefinition<Models.Sticker.Sticker> uuidFilter = builder.Where(sticker => sticker.UUID == uuid);

            FilterDefinition<Models.Sticker.Sticker> keyFilter;

            string firstKey = searchKeys[0];
            if (firstKey.StartsWith("#"))
            {
                keyFilter = builder.Where(sticker => sticker.Tags.Contains(firstKey.Remove(0, 1)));
            }
            else
            {
                keyFilter = builder.Regex(sticker => sticker.Text, firstKey);
            }
            searchKeys.RemoveAt(0);
            foreach (var key in searchKeys)
            {
                if (key.StartsWith("#"))
                {
                    keyFilter |= builder.Where(sticker => sticker.Tags.Contains(key.Remove(0, 1)));
                }
                else
                {
                    keyFilter |= builder.Regex(sticker => sticker.Text, key);
                }
            }

            return await _stickerCollection
                .Find(notDeletedFilter & uuidFilter & keyFilter)
                .Skip(offset)
                .Limit(20)
                .ToListAsync();
        }
        public async Task<List<Models.Sticker.Sticker>> Search(DateTime start, DateTime end,int offset)
        {
            //MongoDB涉及到DateTime需要一律使用UTC进行操作（因为MongoDB默认采用UTC存储时间）
            start = start.ToUniversalTime();
            end = end.ToUniversalTime();

            return await _stickerCollection
                .Find(sticker => !sticker.IsDeleted && sticker.CreatedTime.CompareTo(start)>=0 && sticker.CreatedTime.CompareTo(end) <= 0)
                .Skip(offset)
                .Limit(20)
                .ToListAsync();
        }
        public async Task<List<Models.Sticker.Sticker>> Search(DateTime start, DateTime end, int uuid,int offset)
        {
            //MongoDB涉及到DateTime需要一律使用UTC进行操作（因为MongoDB默认采用UTC存储时间）
            start = start.ToUniversalTime();
            end = end.ToUniversalTime();

            return await _stickerCollection
                .Find(sticker => !sticker.IsDeleted && sticker.UUID == uuid && sticker.CreatedTime.CompareTo(start) >= 0 && sticker.CreatedTime.CompareTo(end) <= 0)
                .Skip(offset)
                .Limit(20)
                .ToListAsync();
        }
        public async Task<List<Models.Sticker.Sticker>> Search(List<string> searchKeys, DateTime start,DateTime end,int offset)
        {
            //MongoDB涉及到DateTime需要一律使用UTC进行操作（因为MongoDB默认采用UTC存储时间）
            start = start.ToUniversalTime();
            end = end.ToUniversalTime();

            var builder = Builders<Models.Sticker.Sticker>.Filter;

            FilterDefinition<Models.Sticker.Sticker> notDeletedFilter = builder.Where(sticker => !sticker.IsDeleted);

            FilterDefinition<Models.Sticker.Sticker> dateTimeFilter = builder.Where(sticker => sticker.CreatedTime.CompareTo(start) >= 0 && sticker.CreatedTime.CompareTo(end) <= 0);

            FilterDefinition<Models.Sticker.Sticker> keyFilter;

            string firstKey = searchKeys[0];
            if (firstKey.StartsWith("#"))
            {
                keyFilter = builder.Where(sticker => sticker.Tags.Contains(firstKey.Remove(0, 1)));
            }
            else
            {
                keyFilter = builder.Regex(sticker => sticker.Text, firstKey);
            }
            searchKeys.RemoveAt(0);
            foreach (var key in searchKeys)
            {
                if (key.StartsWith("#"))
                {
                    keyFilter |= builder.Where(sticker => sticker.Tags.Contains(key.Remove(0, 1)));
                }
                else
                {
                    keyFilter |= builder.Regex(sticker => sticker.Text, key);
                }
            }

            return await _stickerCollection
                .Find(notDeletedFilter & dateTimeFilter & keyFilter)
                .Skip(offset)
                .Limit(20)
                .ToListAsync();
        }
        public async Task<List<Models.Sticker.Sticker>> Search(List<string> searchKeys, DateTime start, DateTime end,int uuid,int offset)
        {
            //MongoDB涉及到DateTime需要一律使用UTC进行操作（因为MongoDB默认采用UTC存储时间）
            start = start.ToUniversalTime();
            end = end.ToUniversalTime();

            var builder = Builders<Models.Sticker.Sticker>.Filter;

            FilterDefinition<Models.Sticker.Sticker> notDeletedFilter = builder.Where(sticker => !sticker.IsDeleted);

            FilterDefinition<Models.Sticker.Sticker> uuidFilter = builder.Where(sticker => sticker.UUID == uuid);

            FilterDefinition<Models.Sticker.Sticker> dateTimeFilter = builder.Where(sticker => sticker.CreatedTime.CompareTo(start) >= 0 && sticker.CreatedTime.CompareTo(end) <= 0);

            FilterDefinition<Models.Sticker.Sticker> keyFilter;

            string firstKey = searchKeys[0];
            if (firstKey.StartsWith("#"))
            {
                keyFilter = builder.Where(sticker => sticker.Tags.Contains(firstKey.Remove(0, 1)));
            }
            else
            {
                keyFilter = builder.Regex(sticker => sticker.Text, firstKey);
            }
            searchKeys.RemoveAt(0);
            foreach (var key in searchKeys)
            {
                if (key.StartsWith("#"))
                {
                    keyFilter |= builder.Where(sticker => sticker.Tags.Contains(key.Remove(0, 1)));
                }
                else
                {
                    keyFilter |= builder.Regex(sticker => sticker.Text, key);
                }
            }

            return await _stickerCollection
                .Find(notDeletedFilter & uuidFilter & dateTimeFilter & keyFilter)
                .Skip(offset)
                .Limit(20)
                .ToListAsync();
        }
    }
}
