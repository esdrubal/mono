using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace Mono
{
	public class Symbolicate
	{
		public static int Main (String[] args)
		{
			if (args.Length != 2 && (args[0] == "store-symbols" && args.Length < 3)) {
				Console.Error.WriteLine ("Usage: symbolicate <msym dir> <input file>");
				Console.Error.WriteLine ("       symbolicate store-symbols <msym dir> [<dir>]+");
				return 1;
			}

			if (args[0] == "store-symbols") {
				var msymDir = args[1];
				var lookupDirs = args.Skip (1).ToArray ();

				var symbolManager = new SymbolManager (msymDir);

				symbolManager.StoreSymbols (lookupDirs);

			} else {
				var msymDir = args [0];
				var inputFile = args [1];

				var symbolManager = new SymbolManager (msymDir);

				using (StreamReader r = new StreamReader (inputFile)) {
					var sb = Process (r, symbolManager);
					Console.Write (sb.ToString ());
				}
			}

			return 0;
		}

		public static StringBuilder Process (StreamReader reader, SymbolManager symbolManager)
		{
			List<StackFrameData> stackFrames = new List<StackFrameData>();
			List<StackTraceMetadata> metadata = new List<StackTraceMetadata>();
			StringBuilder sb = new StringBuilder ();
			bool linesEnded = false;

			for (var line = reader.ReadLine (); line != null; line = reader.ReadLine ()) {
				StackFrameData sfData;
				if (!linesEnded && StackFrameData.TryParse (line, out sfData)) {
					stackFrames.Add (sfData);
					continue;
				}

				if (stackFrames.Count > 0) {
					linesEnded = true;

					StackTraceMetadata stMetadata;
					if (StackTraceMetadata.TryParse (line, out stMetadata)) {
						metadata.Add (stMetadata);
						continue;
					}

					DumpStackTrace (symbolManager, sb, stackFrames, metadata);
		
					// Clear lists for next stack trace
					stackFrames.Clear ();
					metadata.Clear ();
				}

				linesEnded = false;

				// Append last line
				sb.AppendLine (line);
			}

			if (stackFrames.Count > 0)
				DumpStackTrace (symbolManager, sb, stackFrames, metadata);

			return sb;
		}

		private static void DumpStackTrace (SymbolManager symbolManager, StringBuilder sb, List<StackFrameData> stackFrames, List<StackTraceMetadata> metadata)
		{
			string aotid = null;
			var aotidMetadata = metadata.FirstOrDefault ( m => m.Id == "AOTID" );
			if (aotidMetadata != null)
				aotid = aotidMetadata.Value;

			var linesMvid = ProcessLinesMVID (metadata);
			var lineNumber = 0;
			foreach (var sfData in stackFrames) {
				string mvid = null;
				if (linesMvid.ContainsKey (lineNumber))
					mvid = linesMvid [lineNumber++];

				symbolManager.TryResolveLocation (sfData, mvid, aotid);

				sb.AppendLine (sfData.ToString ());
			}

			foreach (var m in metadata)
				sb.AppendLine (m.Line);
		}

		private static Dictionary<int, string> ProcessLinesMVID (List<StackTraceMetadata> metadata)
		{
			var linesMvid = new Dictionary<int, string> ();
			var mvidData = metadata.Where ( m => m.Id == "MVID" ).Select ( m => m.Value );
			foreach (var m in mvidData) {
				var s1 = m.Split (new char[] {' '}, 2);
				var mvid = s1 [0];
				var lines = s1 [1].Split (',');
				foreach (var line in lines)
					linesMvid.Add (int.Parse (line), mvid);
			}

			return linesMvid;
		}
	}
}
