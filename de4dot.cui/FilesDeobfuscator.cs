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
using de4dot.blocks;
using de4dot.code;
using de4dot.code.renamer;
using de4dot.code.deobfuscators;
using de4dot.code.AssemblyClient;

namespace de4dot.cui {
	class FilesDeobfuscator {
		Options options;
		IDeobfuscatorContext deobfuscatorContext = new DeobfuscatorContext();

		public class Options {
			public ModuleContext ModuleContext { get; set; }
			public IList<IDeobfuscatorInfo> DeobfuscatorInfos { get; set; }
			public IList<IObfuscatedFile> Files { get; set; }
			public IList<SearchDir> SearchDirs { get; set; }
			public MetaDataFlags MetaDataFlags { get; set; }
			public bool DetectObfuscators { get; set; }
			public RenamerFlags RenamerFlags { get; set; }
			public bool RenameSymbols { get; set; }
			public bool ControlFlowDeobfuscation { get; set; }
			public bool KeepObfuscatorTypes { get; set; }
			public bool OneFileAtATime { get; set; }
			public DecrypterType? DefaultStringDecrypterType { get; set; }
			public List<string> DefaultStringDecrypterMethods { get; private set; }
			public IAssemblyClientFactory AssemblyClientFactory { get; set; }

			public Options() {
				ModuleContext = new ModuleContext(TheAssemblyResolver.Instance);
				DeobfuscatorInfos = new List<IDeobfuscatorInfo>();
				Files = new List<IObfuscatedFile>();
				SearchDirs = new List<SearchDir>();
				DefaultStringDecrypterMethods = new List<string>();
				RenamerFlags = RenamerFlags.RenameNamespaces |
						RenamerFlags.RenameTypes |
						RenamerFlags.RenameProperties |
						RenamerFlags.RenameEvents |
						RenamerFlags.RenameFields |
						RenamerFlags.RenameMethods |
						RenamerFlags.RenameMethodArgs |
						RenamerFlags.RenameGenericParams |
						RenamerFlags.RestorePropertiesFromNames |
						RenamerFlags.RestoreEventsFromNames |
						RenamerFlags.RestoreProperties |
						RenamerFlags.RestoreEvents;
				RenameSymbols = true;
				ControlFlowDeobfuscation = true;
			}
		}

		public class SearchDir {
			public string InputDirectory { get; set; }
			public string OutputDirectory { get; set; }
			public bool SkipUnknownObfuscators { get; set; }
		}

		public FilesDeobfuscator(Options options) {
			this.options = options;
		}

		public void DoIt() {
			if (options.DetectObfuscators)
				DetectObfuscators();
			else if (options.OneFileAtATime)
				DeobfuscateOneAtATime();
			else
				DeobfuscateAll();
		}

		static void RemoveModule(ModuleDef module) {
			TheAssemblyResolver.Instance.Remove(module);
		}

		void DetectObfuscators() {
			foreach (var file in LoadAllFiles(true)) {
				RemoveModule(file.ModuleDefMD);
				file.Dispose();
				deobfuscatorContext.Clear();
			}
		}

		void DeobfuscateOneAtATime() {
			foreach (var file in LoadAllFiles()) {
				int oldIndentLevel = Logger.Instance.IndentLevel;
				try {
					file.DeobfuscateBegin();
					file.Deobfuscate();
					file.DeobfuscateEnd();
					Rename(new List<IObfuscatedFile> { file });
					file.Save();

					RemoveModule(file.ModuleDefMD);
					TheAssemblyResolver.Instance.ClearAll();
					deobfuscatorContext.Clear();
				}
				catch (Exception ex) {
					Logger.Instance.Log(false, null, LoggerEvent.Warning, "Could not deobfuscate {0}. Use -v to see stack trace", file.Filename);
					Program.PrintStackTrace(ex, LoggerEvent.Verbose);
				}
				finally {
					file.Dispose();
					Logger.Instance.IndentLevel = oldIndentLevel;
				}
			}
		}

		void DeobfuscateAll() {
			var allFiles = new List<IObfuscatedFile>(LoadAllFiles());
			try {
				DeobfuscateAllFiles(allFiles);
				Rename(allFiles);
				SaveAllFiles(allFiles);
			}
			finally {
				foreach (var file in allFiles) {
					if (file != null)
						file.Dispose();
				}
			}
		}

		IEnumerable<IObfuscatedFile> LoadAllFiles() {
			return LoadAllFiles(false);
		}

		IEnumerable<IObfuscatedFile> LoadAllFiles(bool onlyScan) {
			var loader = new DotNetFileLoader(new DotNetFileLoader.Options {
				ModuleContext = options.ModuleContext,
				PossibleFiles  = options.Files,
				SearchDirs = options.SearchDirs,
				CreateDeobfuscators = () => CreateDeobfuscators(),
				DefaultStringDecrypterType = options.DefaultStringDecrypterType,
				DefaultStringDecrypterMethods = options.DefaultStringDecrypterMethods,
				AssemblyClientFactory = options.AssemblyClientFactory,
				DeobfuscatorContext = deobfuscatorContext,
				ControlFlowDeobfuscation = options.ControlFlowDeobfuscation,
				KeepObfuscatorTypes = options.KeepObfuscatorTypes,
				MetaDataFlags = options.MetaDataFlags,
				RenamerFlags = options.RenamerFlags,
				CreateDestinationDir = !onlyScan,
			});

			foreach (var file in loader.Load())
				yield return file;
		}

		class DotNetFileLoader {
			Options options;
			Dictionary<string, bool> allFiles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, bool> visitedDirectory = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

			public class Options {
				public ModuleContext ModuleContext { get; set; }
				public IEnumerable<IObfuscatedFile> PossibleFiles { get; set; }
				public IEnumerable<SearchDir> SearchDirs { get; set; }
				public de4dot.code.Func<IList<IDeobfuscator>> CreateDeobfuscators { get; set; }
				public DecrypterType? DefaultStringDecrypterType { get; set; }
				public List<string> DefaultStringDecrypterMethods { get; set; }
				public IAssemblyClientFactory AssemblyClientFactory { get; set; }
				public IDeobfuscatorContext DeobfuscatorContext { get; set; }
				public bool ControlFlowDeobfuscation { get; set; }
				public bool KeepObfuscatorTypes { get; set; }
				public MetaDataFlags MetaDataFlags { get; set; }
				public RenamerFlags RenamerFlags { get; set; }
				public bool CreateDestinationDir { get; set; }
			}

			public DotNetFileLoader(Options options) {
				this.options = options;
			}

			public IEnumerable<IObfuscatedFile> Load() {
				foreach (var file in options.PossibleFiles) {
					if (Add(file, false, true))
						yield return file;
				}

				foreach (var searchDir in options.SearchDirs) {
					foreach (var file in LoadFiles(searchDir))
						yield return file;
				}
			}

			bool Add(IObfuscatedFile file, bool skipUnknownObfuscator, bool isFromPossibleFiles) {
				var key = Utils.GetFullPath(file.Filename);
				if (allFiles.ContainsKey(key)) {
					Logger.Instance.Log(false, null, LoggerEvent.Warning, "Ingoring duplicate file: {0}", file.Filename);
					return false;
				}
				allFiles[key] = true;

				int oldIndentLevel = Logger.Instance.IndentLevel;
				try {
					file.DeobfuscatorContext = options.DeobfuscatorContext;
					file.Load(options.CreateDeobfuscators());
				}
				catch (NotSupportedException) {
					return false;	// Eg. unsupported architecture
				}
				catch (BadImageFormatException) {
					if (isFromPossibleFiles)
						Logger.Instance.Log(false, null, LoggerEvent.Warning, "The file isn't a .NET PE file: {0}", file.Filename);
					return false;	// Not a .NET file
				}
				catch (EndOfStreamException) {
					return false;
				}
				catch (IOException) {
					if (isFromPossibleFiles)
						Logger.Instance.Log(false, null, LoggerEvent.Warning, "The file isn't a .NET PE file: {0}", file.Filename);
					return false;	// Not a .NET file
				}
				catch (Exception ex) {
					Logger.Instance.Log(false, null, LoggerEvent.Warning, "Could not load file ({0}): {1}", ex.GetType(), file.Filename);
					return false;
				}
				finally {
					Logger.Instance.IndentLevel = oldIndentLevel;
				}

				var deob = file.Deobfuscator;
				if (skipUnknownObfuscator && deob.Type == "un") {
					Logger.v("Skipping unknown obfuscator: {0}", file.Filename);
					RemoveModule(file.ModuleDefMD);
					return false;
				}
				else {
					Logger.n("Detected {0} ({1})", deob.Name, file.Filename);
					if (options.CreateDestinationDir)
						CreateDirectories(Path.GetDirectoryName(file.NewFilename));
					return true;
				}
			}

			IEnumerable<IObfuscatedFile> LoadFiles(SearchDir searchDir) {
				DirectoryInfo di = null;
				bool ok = false;
				try {
					di = new DirectoryInfo(searchDir.InputDirectory);
					if (di.Exists)
						ok = true;
				}
				catch (System.Security.SecurityException) {
				}
				catch (ArgumentException) {
				}
				if (ok) {
					foreach (var filename in DoDirectoryInfo(searchDir, di)) {
						var obfuscatedFile = CreateObfuscatedFile(searchDir, filename);
						if (obfuscatedFile != null)
							yield return obfuscatedFile;
					}					
				}
			}

			IEnumerable<string> RecursiveAdd(SearchDir searchDir, IEnumerable<FileSystemInfo> fileSystemInfos) {
				foreach (var fsi in fileSystemInfos) {
					if ((int)(fsi.Attributes & System.IO.FileAttributes.Directory) != 0) {
						foreach (var filename in DoDirectoryInfo(searchDir, (DirectoryInfo)fsi))
							yield return filename;
					}
					else {
						var fi = (FileInfo)fsi;
						if (fi.Exists)
							yield return fi.FullName;
					}
				}
			}

			IEnumerable<string> DoDirectoryInfo(SearchDir searchDir, DirectoryInfo di) {
				if (!di.Exists)
					return new List<string>();

				if (visitedDirectory.ContainsKey(di.FullName))
					return new List<string>();
				visitedDirectory[di.FullName] = true;

				FileSystemInfo[] fsinfos;
				try {
					fsinfos = di.GetFileSystemInfos();
				}
				catch (UnauthorizedAccessException) {
					return new List<string>();
				}
				catch (IOException) {
					return new List<string>();
				}
				catch (System.Security.SecurityException) {
					return new List<string>();
				}
				return RecursiveAdd(searchDir, fsinfos);
			}

			IObfuscatedFile CreateObfuscatedFile(SearchDir searchDir, string filename) {
				var fileOptions = new ObfuscatedFile.Options {
					Filename = Utils.GetFullPath(filename),
					ControlFlowDeobfuscation = options.ControlFlowDeobfuscation,
					KeepObfuscatorTypes = options.KeepObfuscatorTypes,
					MetaDataFlags = options.MetaDataFlags,
					RenamerFlags = options.RenamerFlags,
				};
				if (options.DefaultStringDecrypterType != null)
					fileOptions.StringDecrypterType = options.DefaultStringDecrypterType.Value;
				fileOptions.StringDecrypterMethods.AddRange(options.DefaultStringDecrypterMethods);

				if (!string.IsNullOrEmpty(searchDir.OutputDirectory)) {
					var inDir = Utils.GetFullPath(searchDir.InputDirectory);
					var outDir = Utils.GetFullPath(searchDir.OutputDirectory);

					if (!Utils.StartsWith(fileOptions.Filename, inDir, StringComparison.OrdinalIgnoreCase))
						throw new UserException(string.Format("Filename {0} does not start with inDir {1}", fileOptions.Filename, inDir));

					var subDirs = fileOptions.Filename.Substring(inDir.Length);
					if (subDirs.Length > 0 && subDirs[0] == Path.DirectorySeparatorChar)
						subDirs = subDirs.Substring(1);
					fileOptions.NewFilename = Utils.GetFullPath(Path.Combine(outDir, subDirs));

					if (fileOptions.Filename.Equals(fileOptions.NewFilename, StringComparison.OrdinalIgnoreCase))
						throw new UserException(string.Format("Input and output filename is the same: {0}", fileOptions.Filename));
				}

				var obfuscatedFile = new ObfuscatedFile(fileOptions, options.ModuleContext, options.AssemblyClientFactory);
				if (Add(obfuscatedFile, searchDir.SkipUnknownObfuscators, false))
					return obfuscatedFile;
				obfuscatedFile.Dispose();
				return null;
			}

			void CreateDirectories(string path) {
				if (string.IsNullOrEmpty(path))
					return;
				try {
					var di = new DirectoryInfo(path);
					if (!di.Exists)
						di.Create();
				}
				catch (System.Security.SecurityException) {
				}
				catch (ArgumentException) {
				}
			}
		}

		void DeobfuscateAllFiles(IEnumerable<IObfuscatedFile> allFiles) {
			try {
				foreach (var file in allFiles)
					file.DeobfuscateBegin();
				foreach (var file in allFiles) {
					file.Deobfuscate();
					file.DeobfuscateEnd();
				}
			}
			finally {
				foreach (var file in allFiles)
					file.DeobfuscateCleanUp();
			}
		}

		void SaveAllFiles(IEnumerable<IObfuscatedFile> allFiles) {
			foreach (var file in allFiles)
				file.Save();
		}

		IList<IDeobfuscator> CreateDeobfuscators() {
			var list = new List<IDeobfuscator>(options.DeobfuscatorInfos.Count);
			foreach (var info in options.DeobfuscatorInfos)
				list.Add(info.CreateDeobfuscator());
			return list;
		}

		void Rename(IEnumerable<IObfuscatedFile> theFiles) {
			if (!options.RenameSymbols)
				return;
			var renamer = new Renamer(deobfuscatorContext, theFiles, options.RenamerFlags);
			renamer.Rename();
		}
	}
}
