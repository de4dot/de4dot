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
using System.IO;
using System.Collections.Generic;
using de4dot.code;
using de4dot.code.deobfuscators;
using de4dot.code.AssemblyClient;

namespace de4dot.cui {
	class CommandLineParser {
		static Infos stringDecrypterTypes = new Infos();

		ObfuscatedFile.Options newFileOptions = null;
		IList<IObfuscatedFile> files = new List<IObfuscatedFile>();
		Dictionary<string, Option> optionsDict = new Dictionary<string, Option>(StringComparer.Ordinal);
		IList<IDeobfuscatorInfo> deobfuscatorInfos;
		IList<Option> miscOptions = new List<Option>();
		IList<Option> fileOptions = new List<Option>();
		Option defaultOption;
		FilesDeobfuscator.Options filesOptions;
		FilesDeobfuscator.SearchDir searchDir;
		DecrypterType? defaultStringDecrypterType;
		List<string> defaultStringDecrypterMethods = new List<string>();

		class Info {
			public object value;
			public string name;
			public string desc;

			public Info(object value, string name, string desc) {
				this.value = value;
				this.name = name;
				this.desc = desc;
			}
		}

		class Infos {
			List<Info> infos = new List<Info>();

			public void add(object value, string name, string desc) {
				infos.Add(new Info(value, name, desc));
			}

			public IEnumerable<Info> getInfos() {
				return infos;
			}

			public bool getValue(string name, out object value) {
				foreach (var info in infos) {
					if (name.Equals(info.name, StringComparison.OrdinalIgnoreCase)) {
						value = info.value;
						return true;
					}
				}
				value = null;
				return false;
			}
		}

		static CommandLineParser() {
			stringDecrypterTypes.add(DecrypterType.None, "none", "Don't decrypt strings");
			stringDecrypterTypes.add(DecrypterType.Default, "default", "Use default string decrypter type (usually static)");
			stringDecrypterTypes.add(DecrypterType.Static, "static", "Use static string decrypter if available");
			stringDecrypterTypes.add(DecrypterType.Delegate, "delegate", "Use a delegate to call the real string decrypter");
			stringDecrypterTypes.add(DecrypterType.Emulate, "emulate", "Call real string decrypter and emulate certain instructions");
		}

		public CommandLineParser(IList<IDeobfuscatorInfo> deobfuscatorInfos, FilesDeobfuscator.Options filesOptions) {
			this.deobfuscatorInfos = deobfuscatorInfos;
			this.filesOptions = filesOptions;
			this.filesOptions.DeobfuscatorInfos = deobfuscatorInfos;
			this.filesOptions.AssemblyClientFactory = new NewAppDomainAssemblyClientFactory();

			addAllOptions();
		}

		void addAllOptions() {
			miscOptions.Add(new OneArgOption("r", null, "Scan for .NET files in all subdirs", "dir", (val) => {
				addSearchDir();
				searchDir = new FilesDeobfuscator.SearchDir();
				if (!Utils.pathExists(val))
					exitError(string.Format("Directory {0} does not exist", val));
				searchDir.InputDirectory = val;
			}));
			miscOptions.Add(new OneArgOption("ro", null, "Output base dir for recursively found files", "dir", (val) => {
				if (searchDir == null)
					exitError("Missing -r option");
				searchDir.OutputDirectory = val;
			}));
			miscOptions.Add(new NoArgOption("ru", null, "Skip recursively found files with unsupported obfuscator", () => {
				if (searchDir == null)
					exitError("Missing -r option");
				searchDir.SkipUnknownObfuscators = true;
			}));
			miscOptions.Add(new NoArgOption("d", null, "Detect obfuscators and exit", () => {
				filesOptions.DetectObfuscators = true;
			}));
			miscOptions.Add(new OneArgOption(null, "asm-path", "Add an assembly search path", "path", (val) => {
				AssemblyResolver.Instance.addSearchDirectory(val);
			}));
			miscOptions.Add(new NoArgOption(null, "dont-rename", "Don't rename classes, methods, etc.", () => {
				filesOptions.RenameSymbols = false;
			}));
			miscOptions.Add(new NoArgOption(null, "dont-restore-props", "Don't restore properties/events", () => {
				filesOptions.RestorePropsEvents = false;
			}));
			miscOptions.Add(new OneArgOption(null, "default-strtyp", "Default string decrypter type", "type", (val) => {
				object decrypterType;
				if (!stringDecrypterTypes.getValue(val, out decrypterType))
					exitError(string.Format("Invalid string decrypter type '{0}'", val));
				defaultStringDecrypterType = (DecrypterType)decrypterType;
			}));
			miscOptions.Add(new OneArgOption(null, "default-strtok", "Default string decrypter method token or [type::][name][(args,...)]", "method", (val) => {
				defaultStringDecrypterMethods.Add(val);
			}));
			miscOptions.Add(new NoArgOption(null, "no-cflow-deob", "No control flow deobfuscation (NOT recommended)", () => {
				filesOptions.ControlFlowDeobfuscation = false;
			}));
			miscOptions.Add(new NoArgOption(null, "load-new-process", "Load executed assemblies into a new process", () => {
				filesOptions.AssemblyClientFactory = new NewProcessAssemblyClientFactory();
			}));
			miscOptions.Add(new NoArgOption(null, "keep-types", "Keep obfuscator types, fields, methods", () => {
				filesOptions.KeepObfuscatorTypes = true;
			}));
			miscOptions.Add(new NoArgOption(null, "one-file", "Deobfuscate one file at a time", () => {
				filesOptions.OneFileAtATime = true;
			}));
			miscOptions.Add(new NoArgOption("v", null, "Verbose", () => {
				Log.logLevel = Log.LogLevel.verbose;
			}));
			miscOptions.Add(new NoArgOption("vv", null, "Very verbose", () => {
				Log.logLevel = Log.LogLevel.veryverbose;
			}));
			miscOptions.Add(new NoArgOption("h", "help", "Show this help message", () => {
				usage();
				exit(0);
			}));

			defaultOption = new OneArgOption("f", null, "Name of .NET file", "file", (val) => {
				addFile();
				if (!Utils.fileExists(val))
					exitError(string.Format("File \"{0}\" does not exist.", val));
				newFileOptions = new ObfuscatedFile.Options {
					Filename = val,
					ControlFlowDeobfuscation = filesOptions.ControlFlowDeobfuscation,
					KeepObfuscatorTypes = filesOptions.KeepObfuscatorTypes,
				};
				if (defaultStringDecrypterType != null)
					newFileOptions.StringDecrypterType = defaultStringDecrypterType.Value;
				newFileOptions.StringDecrypterMethods.AddRange(defaultStringDecrypterMethods);
			});
			fileOptions.Add(defaultOption);
			fileOptions.Add(new OneArgOption("o", null, "Name of output file", "file", (val) => {
				if (newFileOptions == null)
					exitError("Missing input file");
				if (string.Equals(Utils.getFullPath(newFileOptions.Filename), Utils.getFullPath(val), StringComparison.OrdinalIgnoreCase))
					exitError(string.Format("Output file can't be same as input file ({0})", val));
				newFileOptions.NewFilename = val;
			}));
			fileOptions.Add(new OneArgOption("p", null, "Obfuscator type (see below)", "type", (val) => {
				if (newFileOptions == null)
					exitError("Missing input file");
				if (!isValidObfuscatorType(val))
					exitError(string.Format("Invalid obfuscator type '{0}'", val));
				newFileOptions.ForcedObfuscatorType = val;
			}));
			fileOptions.Add(new OneArgOption(null, "strtyp", "String decrypter type", "type", (val) => {
				if (newFileOptions == null)
					exitError("Missing input file");
				object decrypterType;
				if (!stringDecrypterTypes.getValue(val, out decrypterType))
					exitError(string.Format("Invalid string decrypter type '{0}'", val));
				newFileOptions.StringDecrypterType = (DecrypterType)decrypterType;
			}));
			fileOptions.Add(new OneArgOption(null, "strtok", "String decrypter method token or [type::][name][(args,...)]", "method", (val) => {
				if (newFileOptions == null)
					exitError("Missing input file");
				newFileOptions.StringDecrypterMethods.Add(val);
			}));

			addOptions(miscOptions);
			addOptions(fileOptions);
			foreach (var info in deobfuscatorInfos)
				addOptions(info.getOptions());
		}

		void addOptions(IEnumerable<Option> options) {
			foreach (var option in options) {
				addOption(option, option.ShortName);
				addOption(option, option.LongName);
			}
		}

		void addOption(Option option, string name) {
			if (name == null)
				return;
			if (optionsDict.ContainsKey(name))
				throw new ApplicationException(string.Format("Option {0} is present twice!", name));
			optionsDict[name] = option;
		}

		public void parse(string[] args) {
			if (args.Length == 0) {
				usage();
				exit(1);
			}

			for (int i = 0; i < args.Length; i++) {
				var arg = args[i];

				string val = null;
				Option option;
				if (optionsDict.TryGetValue(arg, out option)) {
					if (option.NeedArgument) {
						if (++i >= args.Length)
							exitError("Missing options value");
						val = args[i];
					}
				}
				else {
					option = defaultOption;
					val = arg;
				}

				string errorString;
				if (!option.set(val, out errorString))
					exitError(errorString);
			}
			addFile();
			addSearchDir();
			filesOptions.Files = files;
			filesOptions.DefaultStringDecrypterMethods.AddRange(defaultStringDecrypterMethods);
			filesOptions.DefaultStringDecrypterType = defaultStringDecrypterType;
		}

		void addFile() {
			if (newFileOptions == null)
				return;
			files.Add(new ObfuscatedFile(newFileOptions, filesOptions.AssemblyClientFactory));
			newFileOptions = null;
		}

		void addSearchDir() {
			if (searchDir == null)
				return;
			filesOptions.SearchDirs.Add(searchDir);
			searchDir = null;
		}

		bool isValidObfuscatorType(string type) {
			foreach (var info in deobfuscatorInfos) {
				if (string.Equals(info.Type, type, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		void exitError(string msg) {
			usage();
			Log.e("\n\nERROR: {0}\n", msg);
			exit(2);
		}

		void exit(int exitCode) {
			throw new ExitException(exitCode);
		}

		void usage() {
			string progName = getProgramBaseName();
			Log.n("Some of the advanced options may be incompatible, causing a nice exception.");
			Log.n("With great power comes great responsibility.");
			Log.n("");
			Log.n("{0} <options> <file options>", progName);
			Log.n("Options:");
			foreach (var option in miscOptions)
				printOption(option);
			Log.n("");
			Log.n("File options:");
			foreach (var option in fileOptions)
				printOption(option);
			Log.n("");
			Log.n("Deobfuscator options:");
			foreach (var info in deobfuscatorInfos) {
				Log.n("Type {0} ({1})", info.Type, info.Name);
				foreach (var option in info.getOptions())
					printOption(option);
				Log.n("");
			}
			printInfos("String decrypter types", stringDecrypterTypes);
			Log.n("");
			Log.n("Multiple regexes can be used if separated by '{0}'.", NameRegexes.regexSeparatorChar);
			Log.n("Use '{0}' if you want to invert the regex. Example: {0}^[a-z\\d]{{1,2}}${1}{0}^[A-Z]_\\d+${1}^[\\w.]+$", NameRegex.invertChar, NameRegexes.regexSeparatorChar);
			Log.n("");
			Log.n("Examples:");
			Log.n("{0} -r c:\\my\\files -ro c:\\my\\output", progName);
			Log.n("{0} file1 file2 file3", progName);
			Log.n("{0} file1 -f file2 -o file2.out -f file3 -o file3.out", progName);
			Log.n("{0} file1 --strtyp delegate --strtok 06000123", progName);
		}

		string getProgramBaseName() {
			return Utils.getBaseName(Environment.GetCommandLineArgs()[0]);
		}

		void printInfos(string desc, Infos infos) {
			Log.n("{0}", desc);
			foreach (var info in infos.getInfos())
				printOptionAndExplanation(info.name, info.desc);
		}

		void printOption(Option option) {
			string defaultAndDesc;
			if (option.NeedArgument && option.Default != null)
				defaultAndDesc = string.Format("{0} ({1})", option.Description, option.Default);
			else
				defaultAndDesc = option.Description;
			printOptionAndExplanation(getOptionAndArgName(option, option.ShortName ?? option.LongName), defaultAndDesc);
			if (option.ShortName != null && option.LongName != null)
				printOptionAndExplanation(option.LongName, string.Format("Same as {0}", option.ShortName));
		}

		void printOptionAndExplanation(string option, string explanation) {
			const int maxCols = 16;
			const string prefix = "  ";
			string left = string.Format(string.Format("{{0,-{0}}}", maxCols), option);
			if (option.Length > maxCols) {
				Log.n("{0}{1}", prefix, left);
				Log.n("{0}{1} {2}", prefix, new string(' ', maxCols), explanation);
			}
			else
				Log.n("{0}{1} {2}", prefix, left, explanation);
		}

		string getOptionAndArgName(Option option, string optionName) {
			if (option.NeedArgument)
				return optionName + " " + option.ArgumentValueName.ToUpperInvariant();
			else
				return optionName;
		}
	}
}
