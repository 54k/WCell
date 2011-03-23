using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WCell.Util.Logging;
using WCell.Constants;
using WCell.Constants.Achievements;
using WCell.Core.Network;
using WCell.PacketAnalysis;
using WCell.RealmServer.Achievement;
using WCell.RealmServer.Entities;
using WCell.RealmServer.Network;

namespace WCell.RealmServer.Handlers
{
	/// <summary>
	/// Stub class for containing achievement packets
	/// </summary>
	public static class AchievementHandler
	{
        private static Logger log = LogManager.GetCurrentClassLogger();

        //SMSG_ALL_ACHIEVEMENT_DATA
        public static void SendAchievementData(Character chr)
        {
            using (var packet = new RealmPacketOut(RealmServerOpCode.SMSG_ALL_ACHIEVEMENT_DATA, chr.Achievements.AchievementsCount * 3 * 4 + 4))
            {
				if (chr.Achievements.AchievementsCount > 0)
				{
					foreach (AchievementRecord completedAchievement in chr.Achievements.m_completedAchievements.Values)
					{
						packet.WriteUInt((uint) completedAchievement.AchievementEntryId);
						packet.WriteDateTime(completedAchievement.CompleteDate);
					}
					packet.WriteUInt(0xFFFFFFFF);
					chr.Client.Send(packet);
				}
            }
        }

		//SMSG_ACHIEVEMENT_EARNED
		public static void SendAchievementEarned(AchievementEntryId achievementEntryId, Character chr)
		{
			using (var packet = new RealmPacketOut(RealmServerOpCode.SMSG_ACHIEVEMENT_EARNED, 30))
			{
				chr.EntityId.WritePacked(packet);
				packet.WriteUInt((uint)achievementEntryId);
				packet.WriteDateTime(DateTime.Now);
				packet.WriteUInt(0);
				chr.Client.Send(packet);
			}
		}

		public static void SendAchievmentStatus(AchievementCriteriaId achievementCriteriaId, Character chr)
		{
			using (var packet = new RealmPacketOut(RealmServerOpCode.SMSG_CRITERIA_UPDATE, 30))
			{
				packet.WriteUInt((uint)achievementCriteriaId);
				packet.WriteByte(1);
				packet.WriteByte(1); //this is some ID or something maybe merge with previous value ? Maybe it is value for criteria ?
				chr.EntityId.WritePacked(packet);
				packet.WriteUInt(0);
				packet.WriteDateTime(DateTime.Now);
				packet.WriteUInt(0); // Duration
				packet.WriteUInt(0); // Duration left

				chr.Client.Send(packet);
			}
		}
	}
}