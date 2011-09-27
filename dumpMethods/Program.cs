/*
    Copyright (C) 2011 de4dot@gmail.com

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
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace dumpMethods {
	class ProtectedFile {
		string filename;
		string methodsFilename;

		public string Filename {
			get { return filename; }
		}

		public string MethodsFilename {
			get { return methodsFilename; }
			set { methodsFilename = value; }
		}

		public ProtectedFile(string filename, string methodsFilename = null) {
			this.filename = filename;
			this.methodsFilename = methodsFilename;

			if (this.methodsFilename == null)
				this.methodsFilename = this.filename + ".methods";
		}
	}

	class Program {
		[DllImport(@"dumpMethodsN.dll", CallingConvention = CallingConvention.StdCall)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool initialize1(int logLevel, [MarshalAs(UnmanagedType.LPWStr)] string filename);

		[DllImport(@"dumpMethodsN.dll", CallingConvention = CallingConvention.StdCall)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool initialize2();

		[DllImport(@"dumpMethodsN.dll", CallingConvention = CallingConvention.StdCall)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool foundAssembly();

		[DllImport(@"dumpMethodsN.dll", CallingConvention = CallingConvention.StdCall)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool dumpCode([MarshalAs(UnmanagedType.LPWStr)] string methodsFilename);

		delegate void CallCctor();
		static int Main(string[] args) {
			try {
				if (Console.OutputEncoding.IsSingleByte)
					Console.OutputEncoding = new UTF8Encoding(false);

				var files = parseCommandLine(args);
				if (files.Count != 1)
					exitError("Exactly one file must be given.");

				foreach (var file in files) {
					Log.v("calling initialize1()");
					if (!initialize1((int)Log.logLevel, file.Filename))
						fatal("initialize1() failed");

					Log.v("Loading assembly: {0}", file.Filename);
					Assembly assembly = Assembly.LoadFrom(file.Filename);

					loadProtection(assembly);
					if (!foundAssembly())
						fatal("Could not find the assembly in memory");

					Log.v("calling initialize2()");
					if (!initialize2())
						fatal("initialize2() failed");

					Log.n("Dumping all methods...");
					if (!dumpCode(file.MethodsFilename))
						fatal("dumpCode() failed");
					Log.n("Done");
				}
			}
			catch (Exception ex) {
				var line = new string('-', 78);
				Log.e("\n\nERROR: Caught an exception:\n\n");
				Log.e(line);
				Log.e("Message: {0}", ex.Message);
				Log.e("Type: {0}", ex.GetType());
				Log.e(line);
				Log.e("\n\nStack trace:\n{0}", ex.StackTrace);
				if (ex is ReflectionTypeLoadException) {
					var rex = (ReflectionTypeLoadException)ex;
					Log.e("\nReflectionTypeLoadException.LoaderExceptions =");
					foreach (var ex2 in rex.LoaderExceptions)
						Log.e("  {0}", ex2.Message);
				}
				return 1;
			}

			return 0;
		}

		static void exit(int exitCode) {
			Environment.Exit(exitCode);
		}

		static void fatal(string msg) {
			Log.e("{0}", msg);
			exit(3);
		}

		static void loadProtection(Assembly assembly) {
			Log.n("Loading protection...");
			foreach (var type2 in assembly.GetTypes()) {
				if (foundAssembly())
					return;
				var cctor = type2.TypeInitializer;
				if (cctor == null)
					continue;

				// Tell the jitter to compile the method
				RuntimeHelpers.PrepareMethod(cctor.MethodHandle);
			}
		}

		static IList<ProtectedFile> parseCommandLine(string[] args) {
			var files = new List<ProtectedFile>();

			ProtectedFile file = null;
			for (int i = 0; i < args.Length; i++) {
				var arg = args[i];
				var next = i + 1 < args.Length ? args[i + 1] : null;

				if (arg == "-h" || arg == "--help") {
					usage();
					exit(0);
				}
				else if (arg == "-v" || arg == "--verbose") {
					Log.logLevel = Log.LogLevel.verbose;
				}
				else if (arg == "-vv" || arg == "--veryverbose") {
					Log.logLevel = Log.LogLevel.veryverbose;
				}
				else if (arg == "-m" || arg == "--methods") {
					if (next == null)
						exitError("Missing methods filename");
					if (file == null)
						exitError("No input file given yet");
					file.MethodsFilename = next;
					i++;
				}
				else {
					if (arg == "-f" || arg == "--file") {
						if (next == null)
							exitError("Missing filename");
						i++;
						arg = next;
					}

					if (!new FileInfo(arg).Exists)
						exitError(string.Format("File \"{0}\" does not exist.", arg));
					files.Add(file = new ProtectedFile(arg));
				}
			}

			return files;
		}

		static void exitError(string msg) {
			usage();
			Log.e("\n\nERROR: {0}\n", msg);
			exit(2);
		}

		static void usage() {
			string progName = getProgramBaseName();
			Log.n("Dumps encrypted .NET methods. This works only with the supported obfuscators.");
			Log.n("{0} [options] file", progName);
			Log.n("Options:");
			Log.n("    -v          Verbose");
			Log.n("    -vv         Very verbose");
			Log.n("    -f name     Name of .NET file");
			Log.n("    -m name     Name of .methods file");
			Log.n("Examples:");
			Log.n("{0} file1", progName);
			Log.n("{0} -f file1", progName);
			Log.n("{0} -f file1 -m file1.methods", progName);
		}

		static string getProgramBaseName() {
			var name = Environment.GetCommandLineArgs()[0];
			int index = name.LastIndexOf(Path.DirectorySeparatorChar);
			if (index < 0)
				return name;
			return name.Substring(index + 1);
		}
	}
}
