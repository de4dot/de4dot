/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Text;
using de4dot.code;
using de4dot.code.deobfuscators;

namespace de4dot.cui {
	class ExitException : Exception {
		public readonly int code;
		public ExitException(int code) {
			this.code = code;
		}
	}

	class Program {
		static IList<IDeobfuscatorInfo> deobfuscatorInfos = createDeobfuscatorInfos();

		static IList<IDeobfuscatorInfo> createDeobfuscatorInfos() {
			return new List<IDeobfuscatorInfo> {
				new de4dot.code.deobfuscators.Unknown.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.Babel_NET.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.CliSecure.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.CodeVeil.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.CryptoObfuscator.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.DeepSea.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.Dotfuscator.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.dotNET_Reactor.v3.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.dotNET_Reactor.v4.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.Eazfuscator_NET.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.Goliath_NET.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.MaxtoCode.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.Skater_NET.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.SmartAssembly.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.Spices_Net.DeobfuscatorInfo(),
				new de4dot.code.deobfuscators.Xenocode.DeobfuscatorInfo(),
			};
		}

		public static int main(string[] args) {
			int exitCode = 0;

			try {
				if (Console.OutputEncoding.IsSingleByte)
					Console.OutputEncoding = new UTF8Encoding(false);

				Log.n("");
				Log.n("de4dot v{0} Copyright (C) 2011-2012 de4dot@gmail.com", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
				Log.n("Latest version and source code: https://github.com/0xd4d/de4dot");
				Log.n("");

				var options = new FilesDeobfuscator.Options();
				parseCommandLine(args, options);
				new FilesDeobfuscator(options).doIt();
			}
			catch (ExitException ex) {
				exitCode = ex.code;
			}
			catch (UserException ex) {
				Log.e("ERROR: {0}", ex.Message);
				exitCode = 1;
			}
			catch (Exception ex) {
				printStackTrace(ex);
				Log.e("\nTry the latest version before reporting this problem!");
				Log.e("Send bug reports to de4dot@gmail.com or go to https://github.com/0xd4d/de4dot/issues");
				exitCode = 1;
			}

			if (isN00bUser()) {
				Console.Error.WriteLine("\n\nPress any key to exit...\n");
				try {
					Console.ReadKey(true);
				}
				catch (InvalidOperationException) {
				}
			}

			return exitCode;
		}

		static bool isN00bUser() {
			var env = Environment.GetEnvironmentVariables();
			if (env["VisualStudioDir"] != null)
				return false;
			return env["windir"] != null && env["PROMPT"] == null;
		}

		public static void printStackTrace(Exception ex, Log.LogLevel logLevel = Log.LogLevel.error) {
			var line = new string('-', 78);
			Log.log(logLevel, "\n\n");
			Log.log(logLevel, line);
			Log.log(logLevel, "Stack trace:\n{0}", ex.StackTrace);
			Log.log(logLevel, "\n\nERROR: Caught an exception:\n");
			Log.log(logLevel, line);
			Log.log(logLevel, "Message:");
			Log.log(logLevel, "  {0}", ex.Message);
			Log.log(logLevel, "Type:");
			Log.log(logLevel, "  {0}", ex.GetType());
			Log.log(logLevel, line);
		}

		static void parseCommandLine(string[] args, FilesDeobfuscator.Options options) {
			new CommandLineParser(deobfuscatorInfos, options).parse(args);

			Log.vv("Args:");
			Log.indent();
			foreach (var arg in args)
				Log.vv("{0}", Utils.toCsharpString(arg));
			Log.deIndent();
		}
	}
}
