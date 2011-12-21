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
using Mono.Cecil;
using de4dot.blocks;
using de4dot.code.PE;

namespace de4dot.code.deobfuscators.dotNET_Reactor3 {
	// Finds the type that saves the native lib (if in resources) to disk
	class NativeLibSaver {
		ModuleDefinition module;
		TypeDefinition nativeLibCallerType;
		MethodDefinition initMethod;
		Resource nativeFileResource;

		public TypeDefinition Type {
			get { return nativeLibCallerType; }
		}

		public MethodDefinition InitMethod {
			get { return initMethod; }
		}

		public Resource Resource {
			get { return nativeFileResource; }
		}

		public bool Detected {
			get { return nativeLibCallerType != null; }
		}

		public NativeLibSaver(ModuleDefinition module) {
			this.module = module;
		}

		public NativeLibSaver(ModuleDefinition module, NativeLibSaver oldOne) {
			this.module = module;
			this.nativeLibCallerType = lookup(oldOne.nativeLibCallerType, "Could not find nativeLibCallerType");
			this.initMethod = lookup(oldOne.initMethod, "Could not find initMethod");
			if (oldOne.nativeFileResource != null) {
				this.nativeFileResource = DotNetUtils.getResource(module, oldOne.nativeFileResource.Name);
				if (this.nativeFileResource == null)
					throw new ApplicationException("Could not find nativeFileResource");
			}
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find() {
			foreach (var info in DotNetUtils.getCalledMethods(module, DotNetUtils.getMethod(DotNetUtils.getModuleType(module), ".cctor"))) {
				if (!DotNetUtils.isMethod(info.Item2, "System.Void", "()"))
					continue;
				if (info.Item1.FullName != "<PrivateImplementationDetails>{F1C5056B-0AFC-4423-9B83-D13A26B48869}")
					continue;

				nativeLibCallerType = info.Item1;
				initMethod = info.Item2;
				foreach (var s in DotNetUtils.getCodeStrings(initMethod)) {
					nativeFileResource = DotNetUtils.getResource(module, s);
					if (nativeFileResource != null)
						break;
				}
				return;
			}
		}

		public bool patch(PeImage peImage) {
			try {
				return patch2(peImage);
			}
			catch {
				Log.w("Could not patch the file");
				return false;
			}
		}

		bool patch2(PeImage peImage) {
			uint numPatches = peImage.offsetReadUInt32(peImage.ImageLength - 4);
			uint offset = checked(peImage.ImageLength - 4 - numPatches * 8);

			for (uint i = 0; i < numPatches; i++, offset += 8) {
				uint rva = getValue(peImage.offsetReadUInt32(offset));
				var value = peImage.offsetReadUInt32(offset + 4);

				if (value == 4) {
					i++;
					offset += 8;
					rva = getValue(peImage.offsetReadUInt32(offset));
					value = peImage.offsetReadUInt32(offset + 4);
				}
				else
					value = getValue(value);

				peImage.dotNetSafeWrite(rva, BitConverter.GetBytes(value));
			}

			return true;
		}

		static uint getValue(uint value) {
			const uint magic = 2749;
			value = checked(value - magic);
			if (value % 3 != 0)
				throw new Exception();
			return value / 3;
		}
	}
}
