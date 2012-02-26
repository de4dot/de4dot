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
using Mono.Cecil;
using Mono.MyStuff;
using de4dot.blocks;

namespace de4dot.code {
	class AssemblyModule {
		string filename;
		ModuleDefinition module;

		public AssemblyModule(string filename) {
			this.filename = Utils.getFullPath(filename);
		}

		ReaderParameters getReaderParameters() {
			return new ReaderParameters(ReadingMode.Deferred) {
				AssemblyResolver = AssemblyResolver.Instance
			};
		}

		public ModuleDefinition load() {
			return setModule(ModuleDefinition.ReadModule(filename, getReaderParameters()));
		}

		public ModuleDefinition load(byte[] fileData) {
			return setModule(ModuleDefinition.ReadModule(new MemoryStream(fileData), getReaderParameters()));
		}

		ModuleDefinition setModule(ModuleDefinition newModule) {
			module = newModule;
			AssemblyResolver.Instance.addModule(module);
			module.FullyQualifiedName = filename;
			return module;
		}

		public void save(string newFilename, bool updateMaxStack, IWriterListener writerListener) {
			var writerParams = new WriterParameters() {
				UpdateMaxStack = updateMaxStack,
				WriterListener = writerListener,
			};
			module.Write(newFilename, writerParams);
		}

		public ModuleDefinition reload(byte[] newModuleData, DumpedMethods dumpedMethods) {
			AssemblyResolver.Instance.removeModule(module);
			DotNetUtils.typeCaches.invalidate(module);
			return setModule(ModuleDefinition.ReadModule(new MemoryStream(newModuleData), getReaderParameters(), dumpedMethods));
		}

		public override string ToString() {
			return filename;
		}
	}
}
