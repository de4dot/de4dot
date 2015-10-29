/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using de4dot.code;
using de4dot.code.deobfuscators;
using de4dot.code.AssemblyClient;
using de4dot.code.renamer;

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

			public IEnumerable<Info> GetInfos() {
				return infos;
			}

			public bool GetValue(string name, out object value) {
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

			AddAllOptions();
		}

		void AddAllOptions() {
			miscOptions.Add(new OneArgOption("r", null, "Scan for .NET files in all subdirs", "dir", (val) => {
				AddSearchDir();
				searchDir = new FilesDeobfuscator.SearchDir();
				if (!Utils.PathExists(val))
					ExitError(string.Format("Directory {0} does not exist", val));
				searchDir.InputDirectory = val;
			}));
			miscOptions.Add(new OneArgOption("ro", null, "Output base dir for recursively found files", "dir", (val) => {
				if (searchDir == null)
					ExitError("Missing -r option");
				searchDir.OutputDirectory = val;
			}));
			miscOptions.Add(new NoArgOption("ru", null, "Skip recursively found files with unsupported obfuscator", () => {
				if (searchDir == null)
					ExitError("Missing -r option");
				searchDir.SkipUnknownObfuscators = true;
			}));
			miscOptions.Add(new NoArgOption("d", null, "Detect obfuscators and exit", () => {
				filesOptions.DetectObfuscators = true;
			}));
			miscOptions.Add(new OneArgOption(null, "asm-path", "Add an assembly search path", "path", (val) => {
				TheAssemblyResolver.Instance.AddSearchDirectory(val);
			}));
			miscOptions.Add(new NoArgOption(null, "dont-rename", "Don't rename classes, methods, etc.", () => {
				filesOptions.RenameSymbols = false;
				filesOptions.RenamerFlags = 0;
			}));
			miscOptions.Add(new OneArgOption(null, "keep-names", "Don't rename n(amespaces), t(ypes), p(rops), e(vents), f(ields), m(ethods), a(rgs), g(enericparams), d(elegate fields). Can be combined, eg. efm", "flags", (val) => {
				foreach (var c in val) {
					switch (c) {
					case 'n': filesOptions.RenamerFlags &= ~RenamerFlags.RenameNamespaces; break;
					case 't': filesOptions.RenamerFlags &= ~RenamerFlags.RenameTypes; break;
					case 'p': filesOptions.RenamerFlags &= ~RenamerFlags.RenameProperties; break;
					case 'e': filesOptions.RenamerFlags &= ~RenamerFlags.RenameEvents; break;
					case 'f': filesOptions.RenamerFlags &= ~RenamerFlags.RenameFields; break;
					case 'm': filesOptions.RenamerFlags &= ~RenamerFlags.RenameMethods; break;
					case 'a': filesOptions.RenamerFlags &= ~RenamerFlags.RenameMethodArgs; break;
					case 'g': filesOptions.RenamerFlags &= ~RenamerFlags.RenameGenericParams; break;
					case 'd': filesOptions.RenamerFlags |= RenamerFlags.DontRenameDelegateFields; break;
					default: throw new UserException(string.Format("Unrecognized --keep-names char: '{0}'", c));
					}
				}
			}));
			miscOptions.Add(new NoArgOption(null, "dont-create-params", "Don't create method params when renaming", () => {
				filesOptions.RenamerFlags |= RenamerFlags.DontCreateNewParamDefs;
			}));
			miscOptions.Add(new NoArgOption(null, "dont-restore-props", "Don't restore properties/events", () => {
				filesOptions.RenamerFlags &= ~(RenamerFlags.RestorePropertiesFromNames | RenamerFlags.RestoreEventsFromNames);
			}));
			miscOptions.Add(new OneArgOption(null, "default-strtyp", "Default string decrypter type", "type", (val) => {
				object decrypterType;
				if (!stringDecrypterTypes.GetValue(val, out decrypterType))
					ExitError(string.Format("Invalid string decrypter type '{0}'", val));
				defaultStringDecrypterType = (DecrypterType)decrypterType;
			}));
			miscOptions.Add(new OneArgOption(null, "default-strtok", "Default string decrypter method token or [type::][name][(args,...)]", "method", (val) => {
				defaultStringDecrypterMethods.Add(val);
			}));
			miscOptions.Add(new NoArgOption(null, "no-cflow-deob", "No control flow deobfuscation (NOT recommended)", () => {
				filesOptions.ControlFlowDeobfuscation = false;
			}));
			miscOptions.Add(new NoArgOption(null, "only-cflow-deob", "Only control flow deobfuscation", () => {
				filesOptions.ControlFlowDeobfuscation = true;
				// --strtyp none
				defaultStringDecrypterType = DecrypterType.None;
				// --keep-types
				filesOptions.KeepObfuscatorTypes = true;
				// --preserve-tokens
				filesOptions.MetaDataFlags |= MetaDataFlags.PreserveRids |
						MetaDataFlags.PreserveUSOffsets |
						MetaDataFlags.PreserveBlobOffsets |
						MetaDataFlags.PreserveExtraSignatureData;
				// --dont-rename
				filesOptions.RenameSymbols = false;
				filesOptions.RenamerFlags = 0;
			}));
			miscOptions.Add(new NoArgOption(null, "load-new-process", "Load executed assemblies into a new process", () => {
				filesOptions.AssemblyClientFactory = new NewProcessAssemblyClientFactory();
			}));
			miscOptions.Add(new NoArgOption(null, "keep-types", "Keep obfuscator types, fields, methods", () => {
				filesOptions.KeepObfuscatorTypes = true;
			}));
			miscOptions.Add(new NoArgOption(null, "preserve-tokens", "Preserve important tokens, #US, #Blob, extra sig data", () => {
				filesOptions.MetaDataFlags |= MetaDataFlags.PreserveRids |
						MetaDataFlags.PreserveUSOffsets |
						MetaDataFlags.PreserveBlobOffsets |
						MetaDataFlags.PreserveExtraSignatureData;
			}));
			miscOptions.Add(new OneArgOption(null, "preserve-table", "Preserve rids in table: tr (TypeRef), td (TypeDef), fd (Field), md (Method), pd (Param), mr (MemberRef), s (StandAloneSig), ed (Event), pr (Property), ts (TypeSpec), ms (MethodSpec), all (all previous tables). Use - to disable (eg. all,-pd). Can be combined: ed,fd,md", "flags", (val) => {
				foreach (var t in val.Split(',')) {
					var s = t.Trim();
					if (s.Length == 0)
						continue;
					bool clear = s[0] == '-';
					if (clear)
						s = s.Substring(1);
					MetaDataFlags flag;
					switch (s.Trim()) {
					case "": flag = 0; break;
					case "all": flag = MetaDataFlags.PreserveRids; break;
					case "tr": flag = MetaDataFlags.PreserveTypeRefRids; break;
					case "td": flag = MetaDataFlags.PreserveTypeDefRids; break;
					case "fd": flag = MetaDataFlags.PreserveFieldRids; break;
					case "md": flag = MetaDataFlags.PreserveMethodRids; break;
					case "pd": flag = MetaDataFlags.PreserveParamRids; break;
					case "mr": flag = MetaDataFlags.PreserveMemberRefRids; break;
					case "s": flag = MetaDataFlags.PreserveStandAloneSigRids; break;
					case "ed": flag = MetaDataFlags.PreserveEventRids; break;
					case "pr": flag = MetaDataFlags.PreservePropertyRids; break;
					case "ts": flag = MetaDataFlags.PreserveTypeSpecRids; break;
					case "ms": flag = MetaDataFlags.PreserveMethodSpecRids; break;
					default: throw new UserException(string.Format("Invalid --preserve-table option: {0}", s));
					}
					if (clear)
						filesOptions.MetaDataFlags &= ~flag;
					else
						filesOptions.MetaDataFlags |= flag;
				}
			}));
			miscOptions.Add(new NoArgOption(null, "preserve-strings", "Preserve #Strings heap offsets", () => {
				filesOptions.MetaDataFlags |= MetaDataFlags.PreserveStringsOffsets;
			}));
			miscOptions.Add(new NoArgOption(null, "preserve-us", "Preserve #US heap offsets", () => {
				filesOptions.MetaDataFlags |= MetaDataFlags.PreserveUSOffsets;
			}));
			miscOptions.Add(new NoArgOption(null, "preserve-blob", "Preserve #Blob heap offsets", () => {
				filesOptions.MetaDataFlags |= MetaDataFlags.PreserveBlobOffsets;
			}));
			miscOptions.Add(new NoArgOption(null, "preserve-sig-data", "Preserve extra data at the end of signatures", () => {
				filesOptions.MetaDataFlags |= MetaDataFlags.PreserveExtraSignatureData;
			}));
			miscOptions.Add(new NoArgOption(null, "one-file", "Deobfuscate one file at a time", () => {
				filesOptions.OneFileAtATime = true;
			}));
			miscOptions.Add(new NoArgOption("v", null, "Verbose", () => {
				Logger.Instance.MaxLoggerEvent = LoggerEvent.Verbose;
				Logger.Instance.CanIgnoreMessages = false;
			}));
			miscOptions.Add(new NoArgOption("vv", null, "Very verbose", () => {
				Logger.Instance.MaxLoggerEvent = LoggerEvent.VeryVerbose;
				Logger.Instance.CanIgnoreMessages = false;
			}));
			miscOptions.Add(new NoArgOption("h", "help", "Show this help message", () => {
				Usage();
				Exit(0);
			}));

			defaultOption = new OneArgOption("f", null, "Name of .NET file", "file", (val) => {
				AddFile();
				if (!Utils.FileExists(val))
					ExitError(string.Format("File \"{0}\" does not exist.", val));
				newFileOptions = new ObfuscatedFile.Options {
					Filename = val,
					ControlFlowDeobfuscation = filesOptions.ControlFlowDeobfuscation,
					KeepObfuscatorTypes = filesOptions.KeepObfuscatorTypes,
					MetaDataFlags = filesOptions.MetaDataFlags,
					RenamerFlags = filesOptions.RenamerFlags,
				};
				if (defaultStringDecrypterType != null)
					newFileOptions.StringDecrypterType = defaultStringDecrypterType.Value;
				newFileOptions.StringDecrypterMethods.AddRange(defaultStringDecrypterMethods);
			});
			fileOptions.Add(defaultOption);
			fileOptions.Add(new OneArgOption("o", null, "Name of output file", "file", (val) => {
				if (newFileOptions == null)
					ExitError("Missing input file");
				var newFilename = Utils.GetFullPath(val);
				if (string.Equals(Utils.GetFullPath(newFileOptions.Filename), newFilename, StringComparison.OrdinalIgnoreCase))
					ExitError(string.Format("Output file can't be same as input file ({0})", newFilename));
				newFileOptions.NewFilename = newFilename;
			}));
			fileOptions.Add(new OneArgOption("p", null, "Obfuscator type (see below)", "type", (val) => {
				if (newFileOptions == null)
					ExitError("Missing input file");
				if (!IsValidObfuscatorType(val))
					ExitError(string.Format("Invalid obfuscator type '{0}'", val));
				newFileOptions.ForcedObfuscatorType = val;
			}));
			fileOptions.Add(new OneArgOption(null, "strtyp", "String decrypter type", "type", (val) => {
				if (newFileOptions == null)
					ExitError("Missing input file");
				object decrypterType;
				if (!stringDecrypterTypes.GetValue(val, out decrypterType))
					ExitError(string.Format("Invalid string decrypter type '{0}'", val));
				newFileOptions.StringDecrypterType = (DecrypterType)decrypterType;
			}));
			fileOptions.Add(new OneArgOption(null, "strtok", "String decrypter method token or [type::][name][(args,...)]", "method", (val) => {
				if (newFileOptions == null)
					ExitError("Missing input file");
				newFileOptions.StringDecrypterMethods.Add(val);
			}));

			AddOptions(miscOptions);
			AddOptions(fileOptions);
			foreach (var info in deobfuscatorInfos)
				AddOptions(info.GetOptions());
		}

		void AddOptions(IEnumerable<Option> options) {
			foreach (var option in options) {
				AddOption(option, option.ShortName);
				AddOption(option, option.LongName);
			}
		}

		void AddOption(Option option, string name) {
			if (name == null)
				return;
			if (optionsDict.ContainsKey(name))
				throw new ApplicationException(string.Format("Option {0} is present twice!", name));
			optionsDict[name] = option;
		}

		public void Parse(string[] args) {
			if (args.Length == 0) {
				Usage();
				Exit(1);
			}

			for (int i = 0; i < args.Length; i++) {
				var arg = args[i];

				string val = null;
				Option option;
				if (optionsDict.TryGetValue(arg, out option)) {
					if (option.NeedArgument) {
						if (++i >= args.Length)
							ExitError("Missing options value");
						val = args[i];
					}
				}
				else {
					option = defaultOption;
					val = arg;
				}

				string errorString;
				if (!option.Set(val, out errorString))
					ExitError(errorString);
			}
			AddFile();
			AddSearchDir();
			filesOptions.Files = files;
			filesOptions.DefaultStringDecrypterMethods.AddRange(defaultStringDecrypterMethods);
			filesOptions.DefaultStringDecrypterType = defaultStringDecrypterType;
		}

		void AddFile() {
			if (newFileOptions == null)
				return;
			files.Add(new ObfuscatedFile(newFileOptions, filesOptions.ModuleContext, filesOptions.AssemblyClientFactory));
			newFileOptions = null;
		}

		void AddSearchDir() {
			if (searchDir == null)
				return;
			filesOptions.SearchDirs.Add(searchDir);
			searchDir = null;
		}

		bool IsValidObfuscatorType(string type) {
			foreach (var info in deobfuscatorInfos) {
				if (string.Equals(info.Type, type, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		void ExitError(string msg) {
			Usage();
			Logger.Instance.LogErrorDontIgnore("\n\nERROR: {0}\n", msg);
			Exit(2);
		}

		void Exit(int exitCode) {
			throw new ExitException(exitCode);
		}

		void Usage() {
			string progName = GetProgramBaseName();
			Logger.n("Some of the advanced options may be incompatible, causing a nice exception.");
			Logger.n("With great power comes great responsibility.");
			Logger.n("");
			Logger.n("{0} <options> <file options>", progName);
			Logger.n("Options:");
			foreach (var option in miscOptions)
				PrintOption(option);
			Logger.n("");
			Logger.n("File options:");
			foreach (var option in fileOptions)
				PrintOption(option);
			Logger.n("");
			Logger.n("Deobfuscator options:");
			foreach (var info in deobfuscatorInfos) {
				Logger.n("Type {0} ({1})", info.Type, info.Name);
				foreach (var option in info.GetOptions())
					PrintOption(option);
				Logger.n("");
			}
			PrintInfos("String decrypter types", stringDecrypterTypes);
			Logger.n("");
			Logger.n("Multiple regexes can be used if separated by '{0}'.", NameRegexes.regexSeparatorChar);
			Logger.n("Use '{0}' if you want to invert the regex. Example: {0}^[a-z\\d]{{1,2}}${1}{0}^[A-Z]_\\d+${1}^[\\w.]+$", NameRegex.invertChar, NameRegexes.regexSeparatorChar);
			Logger.n("");
			Logger.n("Examples:");
			Logger.n("{0} -r c:\\my\\files -ro c:\\my\\output", progName);
			Logger.n("{0} file1 file2 file3", progName);
			Logger.n("{0} file1 -f file2 -o file2.out -f file3 -o file3.out", progName);
			Logger.n("{0} file1 --strtyp delegate --strtok 06000123", progName);
		}

		string GetProgramBaseName() {
			return Utils.GetBaseName(Environment.GetCommandLineArgs()[0]);
		}

		void PrintInfos(string desc, Infos infos) {
			Logger.n("{0}", desc);
			foreach (var info in infos.GetInfos())
				PrintOptionAndExplanation(info.name, info.desc);
		}

		void PrintOption(Option option) {
			string defaultAndDesc;
			if (option.NeedArgument && option.Default != null)
				defaultAndDesc = string.Format("{0} ({1})", option.Description, option.Default);
			else
				defaultAndDesc = option.Description;
			PrintOptionAndExplanation(GetOptionAndArgName(option, option.ShortName ?? option.LongName), defaultAndDesc);
			if (option.ShortName != null && option.LongName != null)
				PrintOptionAndExplanation(option.LongName, string.Format("Same as {0}", option.ShortName));
		}

		void PrintOptionAndExplanation(string option, string explanation) {
			const int maxCols = 16;
			const string prefix = "  ";
			string left = string.Format(string.Format("{{0,-{0}}}", maxCols), option);
			if (option.Length > maxCols) {
				Logger.n("{0}{1}", prefix, left);
				Logger.n("{0}{1} {2}", prefix, new string(' ', maxCols), explanation);
			}
			else
				Logger.n("{0}{1} {2}", prefix, left, explanation);
		}

		string GetOptionAndArgName(Option option, string optionName) {
			if (option.NeedArgument)
				return optionName + " " + option.ArgumentValueName.ToUpperInvariant();
			else
				return optionName;
		}
	}
}
