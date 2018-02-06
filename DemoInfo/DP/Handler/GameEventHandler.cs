﻿using DemoInfo.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace DemoInfo.DP.Handler
{
	/// <summary>
	/// This class manages all GameEvents for a demo-parser. 
	/// </summary>
	public static class GameEventHandler
	{
		public static void HandleGameEventList(IEnumerable<GameEventList.Descriptor> gel, DemoParser parser)
		{
			parser.GEH_Descriptors = new Dictionary<int, GameEventList.Descriptor>();
			foreach (var d in gel)
				parser.GEH_Descriptors[d.EventId] = d;
		}

		/// <summary>
		/// Apply the specified rawEvent to the parser.
		/// </summary>
		/// <param name="rawEvent">The raw event.</param>
		/// <param name="parser">The parser to mutate.</param>
		public static void Apply(GameEvent rawEvent, DemoParser parser)
		{
			var descriptors = parser.GEH_Descriptors;
			//previous blind implementation
			var blindPlayers = parser.GEH_BlindPlayers;

			if (descriptors == null)
				return;

			Dictionary<string, object> data;
			var eventDescriptor = descriptors[rawEvent.EventId];

			if (parser.Players.Count == 0 && eventDescriptor.Name != "player_connect")
				return;

			if (eventDescriptor.Name == "round_start") {
				data = MapData (eventDescriptor, rawEvent);

				RoundStartedEventArgs rs = new RoundStartedEventArgs () { 
					TimeLimit = (int)data["timelimit"],
					FragLimit = (int)data["fraglimit"],
					Objective = (string)data["objective"]
				};

				parser.RaiseRoundStart (rs);

			}

			if (eventDescriptor.Name == "cs_win_panel_match")
				parser.RaiseWinPanelMatch();

			if (eventDescriptor.Name == "round_announce_final")
				parser.RaiseRoundFinal();

			if (eventDescriptor.Name == "round_announce_last_round_half")
				parser.RaiseLastRoundHalf();

			if (eventDescriptor.Name == "round_officially_ended")
				parser.RaiseRoundOfficiallyEnd();

			if (eventDescriptor.Name == "round_mvp") {
				data = MapData (eventDescriptor, rawEvent);
			
				RoundMVPEventArgs roundMVPArgs = new RoundMVPEventArgs();
                roundMVPArgs.Player = parser.Players.ContainsKey((int)data["userid"]) ? parser.Players[(int)data["userid"]] : null;
				roundMVPArgs.Reason = (RoundMVPReason)data["reason"];
				
				parser.RaiseRoundMVP (roundMVPArgs);
			}

			if (eventDescriptor.Name == "bot_takeover")
			{
				data = MapData(eventDescriptor, rawEvent);

				BotTakeOverEventArgs botTakeOverArgs = new BotTakeOverEventArgs();
				botTakeOverArgs.Taker = parser.Players.ContainsKey((int)data["userid"]) ? parser.Players[(int)data["userid"]] : null;

				parser.RaiseBotTakeOver(botTakeOverArgs);
			}

			if (eventDescriptor.Name == "round_announce_match_start")
				parser.RaiseRoundAnnounceMatchStarted();

			//if (eventDescriptor.Name != "player_footstep" && eventDescriptor.Name != "weapon_fire" && eventDescriptor.Name != "player_jump") {
			//	Console.WriteLine (eventDescriptor.Name);
			//}

			switch (eventDescriptor.Name) {
			case "weapon_fire":

				data = MapData (eventDescriptor, rawEvent);

				WeaponFiredEventArgs fire = new WeaponFiredEventArgs ();
				fire.Shooter = parser.Players.ContainsKey ((int)data ["userid"]) ? parser.Players [(int)data ["userid"]] : null;
				fire.Weapon = new Equipment ((string)data ["weapon"]);

				if (fire.Shooter != null && fire.Shooter.ActiveWeapon != null && fire.Weapon.Class != EquipmentClass.Grenade) {
					fire.Weapon = fire.Shooter.ActiveWeapon;
				}

				parser.RaiseWeaponFired(fire);
				break;
			case "player_death":
				data = MapData(eventDescriptor, rawEvent);

				PlayerKilledEventArgs kill = new PlayerKilledEventArgs();

                kill.Victim = parser.Players.ContainsKey((int)data["userid"]) ? parser.Players[(int)data["userid"]] : null;
				kill.Killer = parser.Players.ContainsKey((int)data["attacker"]) ? parser.Players[(int)data["attacker"]] : null;
				kill.Assister = parser.Players.ContainsKey((int)data["assister"]) ? parser.Players[(int)data["assister"]] : null;
				kill.Headshot = (bool)data["headshot"];
				kill.Weapon = new Equipment((string)data["weapon"], (string)data["weapon_itemid"]);

				if (kill.Weapon.Weapon == EquipmentElement.World && !parser.GameInfo.WarmupPeriod)
				{
					if (!parser.PlayingParticipants.Contains(kill.Victim))
						return;

					// damage won't show up as player_hurt
					//parser.EventHealthChange[kill.Victim] = kill.Victim.HP;
					// this should only trigger for things like switching teams
					// can also trigger when player dies from fall
					parser.PlayerHurts.Enqueue(Tuple.Create(kill.Victim, kill.Victim, kill.Victim.HP, kill.Weapon.Weapon));
					PlayerHurtEventArgs suicideHurt = new PlayerHurtEventArgs();
					suicideHurt.Health = 0;
					suicideHurt.HealthDamage = kill.Victim.HP;
					suicideHurt.Hitgroup = 0;
					suicideHurt.Player = kill.Victim;
					suicideHurt.Attacker = kill.Victim;
					suicideHurt.Weapon = kill.Weapon;
					parser.RaisePlayerHurt(suicideHurt);
				}

				if (kill.Killer != null && kill.Weapon.Class != EquipmentClass.Grenade
						&& kill.Weapon.Weapon != EquipmentElement.Revolver
						&& kill.Killer.Weapons.Any() && kill.Weapon.Weapon != EquipmentElement.World) {
					#if DEBUG
					if(kill.Weapon.Weapon != kill.Killer.ActiveWeapon.Weapon)
						throw new InvalidDataException();
					#endif
					kill.Weapon = kill.Killer.ActiveWeapon;
				}


				kill.PenetratedObjects = (int)data["penetrated"];

				parser.RaisePlayerKilled(kill);
				break;
			case "player_falldamage":
				data = MapData(eventDescriptor, rawEvent);
				var fallenPlayer = parser.Players.ContainsKey((int)data["userid"]) ? parser.Players[(int)data["userid"]] : null;

				// it's possible for damage to be less than 1 and then it doesn't trigger player_hurt
				// I'm assuming the threshold is 1, but not certain.  Found value of 0.56, so it's at least not rounding up from .5
				if (fallenPlayer != null && (float)data["damage"] >= 1)
					fallenPlayer.IsFallen = true;
				break;
			case "player_hurt":
				data = MapData (eventDescriptor, rawEvent);

				PlayerHurtEventArgs hurt = new PlayerHurtEventArgs ();
				hurt.Player = parser.Players.ContainsKey ((int)data ["userid"]) ? parser.Players [(int)data ["userid"]] : null;
				hurt.Attacker = parser.Players.ContainsKey ((int)data ["attacker"]) ? parser.Players [(int)data ["attacker"]] : null;
				hurt.Health = (int)data ["health"];
				hurt.Armor = (int)data ["armor"];
				hurt.HealthDamage = (int)data ["dmg_health"];
				hurt.ArmorDamage = (int)data ["dmg_armor"];
				hurt.Hitgroup = (Hitgroup)((int)data ["hitgroup"]);
				hurt.Weapon = new Equipment ((string)data ["weapon"], "");

				if (hurt.Attacker != null && hurt.Weapon.Class != EquipmentClass.Grenade && hurt.Attacker.Weapons.Any ()) {
					hurt.Weapon = hurt.Attacker.ActiveWeapon;
				}

				PlayerKilledEventArgs bombKill = null;
				if ((int)data["attacker"] == 0 && (string)data["weapon"] == "")
				{
					hurt.Weapon = new Equipment();

					if (hurt.Player.IsFallen)
					{
						// It's obviously possible for a player to take fall damage and bomb damage on the same tick,
						// but it would require another variable to hold state and there's no way to definitively
						// tell which damage is which.

						hurt.Attacker = hurt.Player;
						hurt.Weapon.Weapon = EquipmentElement.World;
						hurt.Player.IsFallen = false;
					}
					else
					{
						// player was hurt by bomb
						hurt.Attacker = parser.PlantedBomb.Owner;
						hurt.Weapon.Weapon = EquipmentElement.Bomb;
					}

					if (hurt.Health == 0 && hurt.Weapon.Weapon != EquipmentElement.World)
					{
						// deaths by bomb don't trigger player_death event
						// Fairly certain that falling deaths always result in "worldspawn" weapon
						bombKill = new PlayerKilledEventArgs();

						bombKill.Victim = hurt.Player;
						bombKill.Killer = hurt.Attacker;
						bombKill.Headshot = false;
						bombKill.Weapon = hurt.Weapon;
					}
				}

				if (!parser.GameInfo.WarmupPeriod)
				{
					int plyrDmgThisTick = parser.PlayerHurts.Where(p => p.Item2 == hurt.Player).Sum(p => p.Item3);
					int plyrHP = hurt.Player.HP - plyrDmgThisTick;
					int dmg = Math.Min(hurt.HealthDamage, plyrHP);
					parser.PlayerHurts.Enqueue(Tuple.Create(hurt.Attacker, hurt.Player, dmg, hurt.Weapon.Weapon));
				}

				parser.RaisePlayerHurt(hurt);
				if (bombKill != null)
					parser.RaisePlayerKilled(bombKill);
				break;

				#region Nades
			case "player_blind":
				data = MapData(eventDescriptor, rawEvent);

				if (parser.Players.ContainsKey((int)data["userid"])) {
					var blindPlayer = parser.Players.ContainsKey((int)data["userid"]) ? parser.Players[(int)data["userid"]] : null;

					if (blindPlayer != null && blindPlayer.Team != Team.Spectate)
					{
						BlindEventArgs blind = new BlindEventArgs();
						blind.Player = blindPlayer;
						if (data.ContainsKey("attacker") && parser.Players.ContainsKey((int)data["attacker"])) {
							blind.Attacker = parser.Players[(int)data["attacker"]];
						} else {
							blind.Attacker = null;
						}

						if (data.ContainsKey("blind_duration"))
							blind.FlashDuration = (float?)data["blind_duration"];
						else
							blind.FlashDuration = null;

						if (data.ContainsKey("entityid"))
							blind.ProjectileEntityID = (int?)data["entityid"];

						parser.RaiseBlind(blind);
					}

					//previous blind implementation
					blindPlayers.Add(parser.Players[(int)data["userid"]]);
				}

				break;
			case "flashbang_detonate":
				var args = FillNadeEvent<FlashEventArgs>(MapData(eventDescriptor, rawEvent), parser);
				args.FlashedPlayers = blindPlayers.ToArray(); //prev blind implementation
				parser.RaiseFlashExploded(args);
				blindPlayers.Clear(); //prev blind implementation
				break;
			case "hegrenade_detonate":
				parser.RaiseGrenadeExploded(FillNadeEvent<GrenadeEventArgs>(MapData(eventDescriptor, rawEvent), parser));
				break;
			case "decoy_started":
				var decoyData = MapData(eventDescriptor, rawEvent);
				var decoyArgs = FillNadeEvent<DecoyEventArgs>(decoyData, parser);
				parser.RaiseDecoyStart(decoyArgs);
				var decoyEnt = parser.DetonateEntities[(int)decoyData["entityid"]];
				decoyEnt.DetonateState = DetonateState.Detonating;
				decoyEnt.NadeArgs = decoyArgs;
				break;
			case "decoy_detonate":
				var decoyEndData = MapData(eventDescriptor, rawEvent);
				parser.RaiseDecoyEnd(FillNadeEvent<DecoyEventArgs>(decoyEndData, parser));
				parser.DetonateEntities.Remove((int)decoyEndData["entityid"]);
				break;
			case "smokegrenade_detonate":
				var smokeData = MapData(eventDescriptor, rawEvent);
				var smokeArgs = FillNadeEvent<SmokeEventArgs>(smokeData, parser);
				parser.RaiseSmokeStart(smokeArgs);
				var smokeEnt = parser.DetonateEntities[(int)smokeData["entityid"]];
				smokeEnt.DetonateState = DetonateState.Detonating;
				smokeEnt.NadeArgs = smokeArgs;
				break;
			case "smokegrenade_expired":
				var smokeEndData = MapData(eventDescriptor, rawEvent);
				parser.RaiseSmokeEnd(FillNadeEvent<SmokeEventArgs>(smokeEndData, parser));
				parser.DetonateEntities.Remove((int)smokeEndData["entityid"]);
				break;
			case "inferno_startburn":
				var fireData = MapData(eventDescriptor, rawEvent);
				var fireArgs = FillNadeEvent<FireEventArgs>(fireData, parser);
				var fireEnt = new FireDetonateEntity(parser);
				parser.DetonateEntities[(int)fireData["entityid"]] = fireEnt;
				fireEnt.NadeArgs = fireArgs;
				fireEnt.DetonateState = DetonateState.Detonating;
				parser.RaiseFireStart(fireArgs);
				break;
			case "inferno_expire":
				var fireEndData = MapData(eventDescriptor, rawEvent);
				var fireEndArgs = FillNadeEvent<FireEventArgs>(fireEndData, parser);
				int endEntityID = (int)fireEndData["entityid"];
				fireEndArgs.ThrownBy = parser.DetonateEntities[endEntityID].NadeArgs.ThrownBy;
				parser.RaiseFireEnd(fireEndArgs);
				parser.DetonateEntities.Remove(endEntityID);
				break;
				#endregion

			case "player_connect":
				data = MapData (eventDescriptor, rawEvent);

				PlayerInfo player = new PlayerInfo ();
				player.UserID = (int)data ["userid"];
				player.Name = (string)data ["name"];
				player.GUID = (string)data ["networkid"];
				player.XUID = player.GUID == "BOT" ? 0 : GetCommunityID (player.GUID);


				//player.IsFakePlayer = (bool)data["bot"];

				int index = (int)data["index"];

				parser.RawPlayers[index] = player;

				break;
			case "player_disconnect":
				data = MapData(eventDescriptor, rawEvent);

				PlayerDisconnectEventArgs disconnect = new PlayerDisconnectEventArgs();
				disconnect.Player = parser.Players.ContainsKey((int)data["userid"]) ? parser.Players[(int)data["userid"]] : null;
				parser.RaisePlayerDisconnect(disconnect);

				int toDelete = (int)data["userid"];
				for (int i = 0; i < parser.RawPlayers.Length; i++) {

					if (parser.RawPlayers[i] != null && parser.RawPlayers[i].UserID == toDelete) {
						parser.RawPlayers[i] = null;
						break;
					}
				}

				if (parser.Players.ContainsKey(toDelete))
				{
					parser.Players.Remove(toDelete);
				}

				break;

			case "player_team":
				data = MapData(eventDescriptor, rawEvent);
				PlayerTeamEventArgs playerTeamEvent = new PlayerTeamEventArgs();

				Team t = Team.Spectate;

				int team = (int)data["team"];

				if (team == parser.tID)
					t = Team.Terrorist;
				else if (team == parser.ctID)
					t = Team.CounterTerrorist;
				playerTeamEvent.NewTeam = t;

				t = Team.Spectate;
				team = (int)data["oldteam"];
				if (team == parser.tID)
					t = Team.Terrorist;
				else if (team == parser.ctID)
					t = Team.CounterTerrorist;
				playerTeamEvent.OldTeam = t;

				playerTeamEvent.Swapped = parser.Players.ContainsKey((int)data["userid"]) ? parser.Players[(int)data["userid"]] : null;
				playerTeamEvent.IsBot = (bool)data["isbot"];
				playerTeamEvent.Silent = (bool)data["silent"];

				parser.RaisePlayerTeam(playerTeamEvent);
				break;
			}
		}

		private static T FillNadeEvent<T>(Dictionary<string, object> data, DemoParser parser) where T : NadeEventArgs, new()
		{
			var nade = new T();

			nade.EntityID = (int)data["entityid"];

			if (data.ContainsKey("userid") && parser.Players.ContainsKey((int)data["userid"]))
				nade.ThrownBy = parser.Players[(int)data["userid"]];
				
			Vector vec = new Vector();
			vec.X = (float)data["x"];
			vec.Y = (float)data["y"];
			vec.Z = (float)data["z"];
			nade.Position = vec;

			return nade;
		}

		private static Dictionary<string, object> MapData(GameEventList.Descriptor eventDescriptor, GameEvent rawEvent)
		{
			Dictionary<string, object> data = new Dictionary<string, object>();

			for (int i = 0; i < eventDescriptor.Keys.Length; i++)
				data.Add(eventDescriptor.Keys[i].Name, rawEvent.Keys[i]);

			return data;
		}

		private static long GetCommunityID(string steamID)
		{
			long authServer = Convert.ToInt64(steamID.Substring(8, 1));
			long authID = Convert.ToInt64(steamID.Substring(10));
			return (76561197960265728 + (authID * 2) + authServer);
		}
	}
}
