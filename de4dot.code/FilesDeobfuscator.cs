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
using de4dot.renamer;
using de4dot.deobfuscators;
using de4dot.AssemblyClient;

namespace de4dot {
	class FilesDeobfuscator {
		Options options;

		public class Options {
			public IList<IDeobfuscatorInfo> DeobfuscatorInfos { get; set; }
			public IList<IObfuscatedFile> Files { get; set; }
			public IList<SearchDir> SearchDirs { get; set; }
			public bool DetectObfuscators { get; set; }
			public bool RenameSymbols { get; set; }
			public bool ControlFlowDeobfuscation { get; set; }
			public bool KeepObfuscatorTypes { get; set; }
			public bool OneFileAtATime { get; set; }
			public DecrypterType? DefaultStringDecrypterType { get; set; }
			public List<string> DefaultStringDecrypterMethods { get; private set; }
			public IAssemblyClientFactory AssemblyClientFactory { get; set; }

			public Options() {
				DeobfuscatorInfos = new List<IDeobfuscatorInfo>();
				Files = new List<IObfuscatedFile>();
				SearchDirs = new List<SearchDir>();
				DefaultStringDecrypterMethods = new List<string>();
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

		public void doIt() {
			if (options.DetectObfuscators)
				detectObfuscators();
			else if (options.OneFileAtATime)
				deobfuscateOneAtATime();
			else
				deobfuscateAll();
		}

		void detectObfuscators() {
			foreach (var file in loadAllFiles()) {
				AssemblyResolver.Instance.removeModule(file.ModuleDefinition);
			}
		}

		void deobfuscateOneAtATime() {
			foreach (var file in loadAllFiles()) {
				try {
					file.deobfuscateBegin();
					file.deobfuscate();
					file.deobfuscateEnd();

					if (options.RenameSymbols)
						new DefinitionsRenamer(new List<IObfuscatedFile> { file }).renameAll();

					file.save();

					AssemblyResolver.Instance.removeModule(file.ModuleDefinition);
				}
				catch (Exception ex) {
					Log.w("Could not deobfuscate {0}. Use -v to see stack trace", file.Filename);
					Utils.printStackTrace(ex, Log.LogLevel.verbose);
				}
				finally {
					file.deobfuscateCleanUp();
				}
			}
		}

		void deobfuscateAll() {
			var allFiles = new List<IObfuscatedFile>(loadAllFiles());
			deobfuscateAllFiles(allFiles);
			renameAllFiles(allFiles);
			saveAllFiles(allFiles);
		}

		IEnumerable<IObfuscatedFile> loadAllFiles() {
			var loader = new DotNetFileLoader(new DotNetFileLoader.Options {
				PossibleFiles  = options.Files,
				SearchDirs = options.SearchDirs,
				CreateDeobfuscators = () => createDeobfuscators(),
				DefaultStringDecrypterType = options.DefaultStringDecrypterType,
				DefaultStringDecrypterMethods = options.DefaultStringDecrypterMethods,
				AssemblyClientFactory = options.AssemblyClientFactory,
				RenameSymbols = options.RenameSymbols,
				ControlFlowDeobfuscation = options.ControlFlowDeobfuscation,
				KeepObfuscatorTypes = options.KeepObfuscatorTypes,
			});
			return loader.load();
		}

		class DotNetFileLoader {
			Options options;
			Dictionary<string, bool> allFiles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, bool> visitedDirectory = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

			public class Options {
				public IEnumerable<IObfuscatedFile> PossibleFiles { get; set; }
				public IEnumerable<SearchDir> SearchDirs { get; set; }
				public Func<IEnumerable<IDeobfuscator>> CreateDeobfuscators { get; set; }
				public DecrypterType? DefaultStringDecrypterType { get; set; }
				public List<string> DefaultStringDecrypterMethods { get; set; }
				public IAssemblyClientFactory AssemblyClientFactory { get; set; }
				public bool RenameSymbols { get; set; }
				public bool ControlFlowDeobfuscation { get; set; }
				public bool KeepObfuscatorTypes { get; set; }
			}

			public DotNetFileLoader(Options options) {
				this.options = options;
			}

			public IEnumerable<IObfuscatedFile> load() {
				foreach (var file in options.PossibleFiles) {
					if (add(file))
						yield return file;
				}

				foreach (var searchDir in options.SearchDirs) {
					foreach (var file in loadFiles(searchDir))
						yield return file;
				}
			}

			bool add(IObfuscatedFile file, bool skipUnknownObfuscator = false) {
				var key = Utils.getFullPath(file.Filename);
				if (allFiles.ContainsKey(key)) {
					Log.w("Ingoring duplicate file: {0}", file.Filename);
					return false;
				}
				allFiles[key] = true;

				try {
					file.load(options.CreateDeobfuscators());
				}
				catch (NotSupportedException) {
					return false;	// Eg. unsupported architecture
				}
				catch (BadImageFormatException) {
					return false;	// Not a .NET file
				}
				catch (UnauthorizedAccessException) {
					Log.w("Could not load file (not authorized): {0}", file.Filename);
					return false;
				}
				catch (NullReferenceException) {
					Log.w("Could not load file (null ref): {0}", file.Filename);
					return false;
				}
				catch (IOException) {
					Log.w("Could not load file (io exception): {0}", file.Filename);
					return false;
				}

				var deob = file.Deobfuscator;
				if (skipUnknownObfuscator && deob is deobfuscators.Unknown.Deobfuscator) {
					Log.v("Skipping unknown obfuscator: {0}", file.Filename);
					return false;
				}
				else {
					Log.n("Detected {0} ({1})", deob.Name, file.Filename);
					createDirectories(Path.GetDirectoryName(file.NewFilename));
					return true;
				}
			}

			IEnumerable<IObfuscatedFile> loadFiles(SearchDir searchDir) {
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
					foreach (var filename in doDirectoryInfo(searchDir, di)) {
						var obfuscatedFile = createObfuscatedFile(searchDir, filename);
						if (obfuscatedFile != null)
							yield return obfuscatedFile;
					}					
				}
			}

			IEnumerable<string> recursiveAdd(SearchDir searchDir, IEnumerable<FileSystemInfo> fileSystemInfos) {
				foreach (var fsi in fileSystemInfos) {
					if ((int)(fsi.Attributes & FileAttributes.Directory) != 0) {
						foreach (var filename in doDirectoryInfo(searchDir, (DirectoryInfo)fsi))
							yield return filename;
					}
					else {
						var fi = (FileInfo)fsi;
						if (fi.Exists)
							yield return fi.FullName;
					}
				}
			}

			IEnumerable<string> doDirectoryInfo(SearchDir searchDir, DirectoryInfo di) {
				if (!di.Exists)
					return null;

				if (visitedDirectory.ContainsKey(di.FullName))
					return null;
				visitedDirectory[di.FullName] = true;

				FileSystemInfo[] fsinfos;
				try {
					fsinfos = di.GetFileSystemInfos();
				}
				catch (UnauthorizedAccessException) {
					return null;
				}
				catch (IOException) {
					return null;
				}
				return recursiveAdd(searchDir, fsinfos);
			}

			IObfuscatedFile createObfuscatedFile(SearchDir searchDir, string filename) {
				var fileOptions = new ObfuscatedFile.Options {
					Filename = Utils.getFullPath(filename),
					RenameSymbols = options.RenameSymbols,
					ControlFlowDeobfuscation = options.ControlFlowDeobfuscation,
					KeepObfuscatorTypes = options.KeepObfuscatorTypes,
				};
				if (options.DefaultStringDecrypterType != null)
					fileOptions.StringDecrypterType = options.DefaultStringDecrypterType.Value;
				fileOptions.StringDecrypterMethods.AddRange(options.DefaultStringDecrypterMethods);

				if (!string.IsNullOrEmpty(searchDir.OutputDirectory)) {
					var inDir = Utils.getFullPath(searchDir.InputDirectory);
					var outDir = Utils.getFullPath(searchDir.OutputDirectory);

					if (!Utils.StartsWith(fileOptions.Filename, inDir, StringComparison.OrdinalIgnoreCase))
						throw new UserException(string.Format("Filename {0} does not start with inDir {1}", fileOptions.Filename, inDir));

					var subDirs = fileOptions.Filename.Substring(inDir.Length);
					if (subDirs.Length > 0 && subDirs[0] == Path.DirectorySeparatorChar)
						subDirs = subDirs.Substring(1);
					fileOptions.NewFilename = Utils.getFullPath(Path.Combine(outDir, subDirs));

					if (fileOptions.Filename.Equals(fileOptions.NewFilename, StringComparison.OrdinalIgnoreCase))
						throw new UserException(string.Format("Input and output filename is the same: {0}", fileOptions.Filename));
				}

				var obfuscatedFile = new ObfuscatedFile(fileOptions, options.AssemblyClientFactory);
				if (add(obfuscatedFile, searchDir.SkipUnknownObfuscators))
					return obfuscatedFile;
				return null;
			}

			void createDirectories(string path) {
				if (string.IsNullOrEmpty(path))
					return;
				var di = new DirectoryInfo(path);
				if (!di.Exists)
					di.Create();
			}
		}

		void deobfuscateAllFiles(IEnumerable<IObfuscatedFile> allFiles) {
			try {
				foreach (var file in allFiles)
					file.deobfuscateBegin();
				foreach (var file in allFiles) {
					file.deobfuscate();
					file.deobfuscateEnd();
				}
			}
			finally {
				foreach (var file in allFiles)
					file.deobfuscateCleanUp();
			}
		}

		void renameAllFiles(IEnumerable<IObfuscatedFile> allFiles) {
			if (!options.RenameSymbols)
				return;
			new DefinitionsRenamer(allFiles).renameAll();
		}

		void saveAllFiles(IEnumerable<IObfuscatedFile> allFiles) {
			foreach (var file in allFiles)
				file.save();
		}

		IEnumerable<IDeobfuscator> createDeobfuscators() {
			var list = new List<IDeobfuscator>(options.DeobfuscatorInfos.Count);
			foreach (var info in options.DeobfuscatorInfos)
				list.Add(info.createDeobfuscator());
			return list;
		}
	}
}
