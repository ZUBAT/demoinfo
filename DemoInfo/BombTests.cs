using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DemoInfo;

namespace Testing
{
    class BombTests
    {
        static void Main(string[] args)
        {

			ParseDemos(0);
			
        }

		static void ParserSubs(DemoParser parser)
		{
			bool planting = false;
			bool planted = false;
			bool exploded = false;
			bool defusing = false;
			bool defused = false;

			RoundEndReason reason = 0;
			int endTick = 0;

			parser.BombBeginPlant += (sender, e) =>
			{
				if (planting || planted || exploded || defusing || defused || e.Player == null)
					System.Diagnostics.Debugger.Break();

				planting = true;
			};
			parser.BombAbortPlant += (sender, e) =>
			{
				if (!planting || planted || exploded || defusing || defused || e.Player == null)
					System.Diagnostics.Debugger.Break();

				planting = false;
			};
			parser.BombPlanted += (sender, e) =>
			{
				if (!(planting || parser.IngameTick == 0) || planted || exploded || defusing || defused || e.Player == null)
					System.Diagnostics.Debugger.Break();

				planting = false;
				planted = true;
			};
			parser.BombExploded += (sender, e) =>
			{
				if (planting || !planted || exploded || defusing || defused || e.Player == null)
					System.Diagnostics.Debugger.Break();

				planted = false;

				if (endTick == parser.CurrentTick || reason == 0)
					exploded = true;
			};
			parser.BombBeginDefuse += (sender, e) =>
			{
				if (planting || !planted || exploded || defusing || defused || e.Player == null)
					System.Diagnostics.Debugger.Break();

				defusing = true;
			};
			parser.BombAbortDefuse += (sender, e) =>
			{
				if (planting || !planted || exploded || !defusing || defused || e.Player == null)
					System.Diagnostics.Debugger.Break();

				defusing = false;
			};
			parser.BombDefused += (sender, e) =>
			{
				if (planting || !planted || exploded || !defusing || defused || e.Player == null)
					System.Diagnostics.Debugger.Break();

				defused = true;
			};

			parser.RoundEnd += (sender, e) =>
			{
				reason = e.Reason;
				endTick = parser.CurrentTick;

				//if (reason == 0)
				//	System.Diagnostics.Debugger.Break();
			};
			parser.RoundOfficiallyEnd += (sender, e) =>
			{
				if ((reason == RoundEndReason.BombDefused && !defused)
					|| (defused && reason != RoundEndReason.BombDefused)
					|| (reason == RoundEndReason.TargetBombed && !exploded)
					|| (exploded && reason != RoundEndReason.TargetBombed))
				{
					if (reason != 0)
						System.Diagnostics.Debugger.Break();
				}
				planting = false;
				planted = false;
				exploded = false;
				defusing = false;
				defused = false;

				reason = 0;
				endTick = 0;
			};
		}

		static void ParseDemo(string demo)
		{
			using (var fileStream = File.OpenRead(demo))
			{
				using (var parser = new DemoParser(fileStream))
				{
					System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
					sw.Start();
					ParserSubs(parser);
					parser.ParseHeader();
					try
					{
						parser.ParseToEnd();
					}
					catch (System.IO.EndOfStreamException)
					{
						;
					}
					sw.Stop();
					Console.WriteLine(sw.Elapsed);
				}
			}
		}
		static void ParseDemos(int start)
		{
			string archive = "E:\\gamerlytics\\gamerlytics\\gamerlyticsweb\\gamerlyticsweb\\scripts\\parsers\\demos";
			string[] seriesDirs = Directory.GetDirectories(archive);
			Parallel.ForEach(seriesDirs, new ParallelOptions { MaxDegreeOfParallelism = 7 }, seriesDir =>
			{
				foreach (string demo in Directory.GetFiles(seriesDir, "*.dem"))
				{
					ParseDemo(demo);
				}
			});

			Console.WriteLine("FINISHED");
			Console.ReadLine();
		}
    }
}
