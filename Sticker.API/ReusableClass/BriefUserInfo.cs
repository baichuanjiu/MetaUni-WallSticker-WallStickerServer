namespace Sticker.API.ReusableClass
{
    public class BriefUserInfo
    {
        public int UUID { get; set; }
        public string Avatar { get; set; }
        public string Nickname { get; set; }

        public BriefUserInfo()
        {
        }

        public BriefUserInfo(int UUID, string avatar, string nickname)
        {
            this.UUID = UUID;
            Avatar = avatar;
            Nickname = nickname;
        }

        public BriefUserInfo(Protos.BriefUserInfo.BriefUserInfo briefUserInfo)
        {
            UUID = briefUserInfo.UUID;
            Avatar = briefUserInfo.Avatar;
            Nickname = briefUserInfo.Nickname;
        }
    }
}
