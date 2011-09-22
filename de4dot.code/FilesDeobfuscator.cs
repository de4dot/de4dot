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
				loadAllFiles();
			else
				deobfuscateAll();
		}

		void deobfuscateAll() {
			loadAllFiles();
			deobfuscateAllFiles();
			renameAllFiles();
			saveAllFiles();
		}

		void loadAllFiles() {
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
			options.Files = loader.load();
		}

		class DotNetFileLoader {
			Options options;
			Dictionary<string, bool> allFiles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, bool> visitedDirectory = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			IList<IObfuscatedFile> files;

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

			public IList<IObfuscatedFile> load() {
				files = new List<IObfuscatedFile>();

				foreach (var file in options.PossibleFiles)
					add(file);

				foreach (var searchDir in options.SearchDirs)
					recursiveAdd(searchDir);

				return files;
			}

			void add(IObfuscatedFile file, bool skipUnknownObfuscator = false) {
				var key = Utils.getFullPath(file.Filename);
				if (allFiles.ContainsKey(key)) {
					Log.w("Ingoring duplicate file: {0}", file.Filename);
					return;
				}
				allFiles[key] = true;

				try {
					file.load(options.CreateDeobfuscators());
				}
				catch (NotSupportedException) {
					return;		// Eg. unsupported architecture
				}
				catch (BadImageFormatException) {
					return;		// Not a .NET file
				}
				catch (IOException) {
					Log.w("Could not load file: {0}", file.Filename);
					return;
				}

				var deob = file.Deobfuscator;
				if (skipUnknownObfuscator && deob is deobfuscators.Unknown.Deobfuscator) {
					Log.v("Skipping unknown obfuscator: {0}", file.Filename);
				}
				else {
					Log.n("Detected {0} ({1})", deob.Name, file.Filename);
					files.Add(file);
					createDirectories(Path.GetDirectoryName(file.NewFilename));
				}
			}

			void recursiveAdd(SearchDir searchDir) {
				DirectoryInfo di;
				try {
					di = new DirectoryInfo(searchDir.InputDirectory);
					if (!di.Exists)
						return;
				}
				catch (System.Security.SecurityException) {
					return;
				}
				catch (ArgumentException) {
					return;
				}
				doDirectoryInfo(searchDir, di);
			}

			void recursiveAdd(SearchDir searchDir, IEnumerable<FileSystemInfo> fileSystemInfos) {
				foreach (var fsi in fileSystemInfos) {
					if ((int)(fsi.Attributes & FileAttributes.Directory) != 0)
						doDirectoryInfo(searchDir, (DirectoryInfo)fsi);
					else
						doFileInfo(searchDir, (FileInfo)fsi);
				}
			}

			void doDirectoryInfo(SearchDir searchDir, DirectoryInfo di) {
				if (!di.Exists)
					return;

				if (visitedDirectory.ContainsKey(di.FullName))
					return;
				visitedDirectory[di.FullName] = true;

				FileSystemInfo[] fsinfos;
				try {
					fsinfos = di.GetFileSystemInfos();
				}
				catch (UnauthorizedAccessException) {
					return;
				}
				catch (IOException) {
					return;
				}
				recursiveAdd(searchDir, fsinfos);
			}

			void doFileInfo(SearchDir searchDir, FileInfo fi) {
				if (!fi.Exists)
					return;

				var fileOptions = new ObfuscatedFile.Options {
					Filename = Utils.getFullPath(fi.FullName),
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

					if (!fileOptions.Filename.StartsWith(inDir, StringComparison.OrdinalIgnoreCase))
						throw new UserException(string.Format("Filename {0} does not start with inDir {1}", fileOptions.Filename, inDir));

					var subDirs = fileOptions.Filename.Substring(inDir.Length);
					if (subDirs.Length > 0 && subDirs[0] == Path.DirectorySeparatorChar)
						subDirs = subDirs.Substring(1);
					fileOptions.NewFilename = Utils.getFullPath(Path.Combine(outDir, subDirs));

					if (fileOptions.Filename.Equals(fileOptions.NewFilename, StringComparison.OrdinalIgnoreCase))
						throw new UserException(string.Format("Input and output filename is the same: {0}", fileOptions.Filename));
				}

				add(new ObfuscatedFile(fileOptions, options.AssemblyClientFactory), searchDir.SkipUnknownObfuscators);
			}

			void createDirectories(string path) {
				if (string.IsNullOrEmpty(path))
					return;
				var di = new DirectoryInfo(path);
				if (!di.Exists)
					di.Create();
			}
		}

		void deobfuscateAllFiles() {
			try {
				foreach (var file in options.Files)
					file.deobfuscateBegin();
				foreach (var file in options.Files) {
					file.deobfuscate();
					file.deobfuscateEnd();
				}
			}
			finally {
				foreach (var file in options.Files)
					file.deobfuscateCleanUp();
			}
		}

		void renameAllFiles() {
			if (!options.RenameSymbols)
				return;
			new DefinitionsRenamer(options.Files).renameAll();
		}

		void saveAllFiles() {
			foreach (var file in options.Files)
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
