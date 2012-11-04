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
using dot10.DotNet;
using dot10.DotNet.Writer;
using de4dot.blocks;

namespace de4dot.code {
	class AssemblyModule {
		string filename;
		ModuleDefMD module;

		public AssemblyModule(string filename) {
			this.filename = Utils.getFullPath(filename);
		}

		public ModuleDefMD load() {
			return setModule(ModuleDefMD.Load(filename));
		}

		public ModuleDefMD load(byte[] fileData) {
			return setModule(ModuleDefMD.Load(fileData));
		}

		ModuleDefMD setModule(ModuleDefMD newModule) {
			module = newModule;
			TheAssemblyResolver.Instance.addModule(module);
			module.EnableTypeDefFindCache = true;
			module.Location = filename;
			return module;
		}

		public void save(string newFilename, bool updateMaxStack, IModuleWriterListener writerListener) {
			var writerOptions = new ModuleWriterOptions(module, writerListener);
			if (!updateMaxStack)
				writerOptions.MetaDataOptions.Flags |= MetaDataFlags.KeepOldMaxStack;
			module.Write(newFilename, writerOptions);
		}

		public ModuleDefMD reload(byte[] newModuleData, DumpedMethods dumpedMethods) {
			TheAssemblyResolver.Instance.removeModule(module);
			//TODO: Use dumped methods
			return setModule(ModuleDefMD.Load(newModuleData));
		}

		public override string ToString() {
			return filename;
		}
	}
}
