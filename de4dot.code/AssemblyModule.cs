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

using dnlib.DotNet;
using dnlib.DotNet.Writer;

namespace de4dot.code {
	public interface IModuleWriterListener {
		void OnWriterEvent(ModuleWriterBase writer, ModuleWriterEvent evt);
	}

	public class AssemblyModule {
		string filename;
		ModuleDefMD module;
		ModuleContext moduleContext;

		public AssemblyModule(string filename, ModuleContext moduleContext) {
			this.filename = Utils.GetFullPath(filename);
			this.moduleContext = moduleContext;
		}

		public ModuleDefMD Load() {
			var options = new ModuleCreationOptions(moduleContext) { TryToLoadPdbFromDisk = false };
			return SetModule(ModuleDefMD.Load(filename, options));
		}

		public ModuleDefMD Load(byte[] fileData) {
			var options = new ModuleCreationOptions(moduleContext) { TryToLoadPdbFromDisk = false };
			return SetModule(ModuleDefMD.Load(fileData, options));
		}

		ModuleDefMD SetModule(ModuleDefMD newModule) {
			module = newModule;
			TheAssemblyResolver.Instance.AddModule(module);
			module.EnableTypeDefFindCache = true;
			module.Location = filename;
			return module;
		}

		public void Save(string newFilename, MetadataFlags mdFlags, IModuleWriterListener writerListener) {
			if (module.IsILOnly) {
				var writerOptions = new ModuleWriterOptions(module);
				writerOptions.WriterEvent += (s, e) => writerListener?.OnWriterEvent(e.Writer, e.Event);
				writerOptions.MetadataOptions.Flags |= mdFlags;
				writerOptions.Logger = Logger.Instance;
				module.Write(newFilename, writerOptions);
			}
			else {
				var writerOptions = new NativeModuleWriterOptions(module, optimizeImageSize: true);
				writerOptions.WriterEvent += (s, e) => writerListener?.OnWriterEvent(e.Writer, e.Event);
				writerOptions.MetadataOptions.Flags |= mdFlags;
				writerOptions.Logger = Logger.Instance;
				writerOptions.KeepExtraPEData = true;
				writerOptions.KeepWin32Resources = true;
				module.NativeWrite(newFilename, writerOptions);
			}
		}

		public ModuleDefMD Reload(byte[] newModuleData, DumpedMethodsRestorer dumpedMethodsRestorer, IStringDecrypter stringDecrypter) {
			TheAssemblyResolver.Instance.Remove(module);
			var options = new ModuleCreationOptions(moduleContext) { TryToLoadPdbFromDisk = false };
			var mod = ModuleDefMD.Load(newModuleData, options);
			if (dumpedMethodsRestorer != null)
				dumpedMethodsRestorer.Module = mod;
			mod.StringDecrypter = stringDecrypter;
			mod.MethodDecrypter = dumpedMethodsRestorer;
			mod.TablesStream.ColumnReader = dumpedMethodsRestorer;
			mod.TablesStream.MethodRowReader = dumpedMethodsRestorer;
			return SetModule(mod);
		}

		public override string ToString() => filename;
	}
}
