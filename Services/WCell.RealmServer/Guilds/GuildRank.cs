using WCell.Constants;
using WCell.Constants.Guilds;

namespace WCell.RealmServer.Guilds
{
    public partial class GuildRank
    {

        /// <summary>
        /// The daily money withdrawl allowance from the Guild Bank
        /// </summary>
        public uint DailyBankMoneyAllowance
        {
            get { return (uint)_moneyPerDay; }
            set { _moneyPerDay = (int)value; }
        }

        public GuildPrivileges Privileges
        {
            get { return (GuildPrivileges)_privileges; }
            set { _privileges = (int)value; }
        }

        public GuildRank()
		{
			
		}
    }
}