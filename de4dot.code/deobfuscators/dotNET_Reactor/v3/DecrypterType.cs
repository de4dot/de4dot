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
using System.Collections.Generic;
using System.Text;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	// Find the type that decrypts strings and calls the native lib
	class DecrypterType {
		ModuleDefMD module;
		TypeDef decrypterType;
		MethodDef stringDecrypter1;
		MethodDef stringDecrypter2;
		List<MethodDef> initMethods = new List<MethodDef>();
		List<ModuleRef> moduleRefs = new List<ModuleRef>();
		Resource linkedResource;

		public bool Detected {
			get { return decrypterType != null; }
		}

		public TypeDef Type {
			get { return decrypterType; }
		}

		public Resource LinkedResource {
			get { return linkedResource; }
		}

		public MethodDef StringDecrypter1 {
			get { return stringDecrypter1; }
		}

		public MethodDef StringDecrypter2 {
			get { return stringDecrypter2; }
		}

		public IEnumerable<MethodDef> InitMethods {
			get { return initMethods; }
		}

		public IEnumerable<MethodDef> StringDecrypters {
			get {
				return new List<MethodDef> {
					stringDecrypter1,
					stringDecrypter2,
				};
			}
		}

		public DecrypterType(ModuleDefMD module) {
			this.module = module;
		}

		public DecrypterType(ModuleDefMD module, DecrypterType oldOne) {
			this.module = module;
			this.decrypterType = Lookup(oldOne.decrypterType, "Could not find decrypterType");
			this.stringDecrypter1 = Lookup(oldOne.stringDecrypter1, "Could not find stringDecrypter1");
			this.stringDecrypter2 = Lookup(oldOne.stringDecrypter2, "Could not find stringDecrypter2");
			foreach (var method in oldOne.initMethods)
				initMethods.Add(Lookup(method, "Could not find initMethod"));
			UpdateModuleRefs();
		}

		T Lookup<T>(T def, string errorMessage) where T : class, ICodedToken {
			return DeobUtils.Lookup(module, def, errorMessage);
		}

		public void Find() {
			foreach (var type in module.Types) {
				if (type.FullName != "<PrivateImplementationDetails>{B4838DC1-AC79-43d1-949F-41B518B904A8}")
					continue;

				decrypterType = type;
				stringDecrypter1 = GetStringDecrypter(type, "CS$0$0004");
				stringDecrypter2 = GetStringDecrypter(type, "CS$0$0005");
				foreach (var method in type.Methods) {
					if (DotNetUtils.IsMethod(method, "System.Void", "()"))
						initMethods.Add(method);
				}
				UpdateModuleRefs();
				return;
			}
		}

		void UpdateModuleRefs() {
			foreach (var method in decrypterType.Methods) {
				if (method.ImplMap != null) {
					switch (method.ImplMap.Name.String) {
					case "nr_nli":
					case "nr_startup":
						moduleRefs.Add(method.ImplMap.Module);
						break;
					}
				}
			}
			UpdateLinkedResource();
		}

		void UpdateLinkedResource() {
			foreach (var modref in moduleRefs) {
				var resource = DotNetUtils.GetResource(module, modref.Name.String) as LinkedResource;
				if (resource == null)
					continue;

				linkedResource = resource;
				return;
			}
		}

		MethodDef GetStringDecrypter(TypeDef type, string name) {
			var method = type.FindMethod(name);
			if (method == null)
				return null;
			if (!DotNetUtils.IsMethod(method, "System.String", "(System.String)"))
				return null;
			return method;
		}

		public string Decrypt1(string s) {
			var sb = new StringBuilder(s.Length);
			foreach (var c in s)
				sb.Append((char)(0xFF - (byte)c));
			return sb.ToString();
		}

		public string Decrypt2(string s) {
			return Encoding.Unicode.GetString(Convert.FromBase64String(s));
		}

		public bool Patch(byte[] peData) {
			try {
				using (var peImage = new MyPEImage(peData))
					return Patch2(peImage);
			}
			catch {
				Logger.w("Could not patch the file");
				return false;
			}
		}

		bool Patch2(MyPEImage peImage) {
			uint numPatches = peImage.OffsetReadUInt32(peImage.Length - 4);
			uint offset = checked(peImage.Length - 4 - numPatches * 8);

			bool startedPatchingBadData = false;
			for (uint i = 0; i < numPatches; i++, offset += 8) {
				uint rva = GetValue(peImage.OffsetReadUInt32(offset));
				var value = peImage.OffsetReadUInt32(offset + 4);

				if (value == 4) {
					i++;
					offset += 8;
					rva = GetValue(peImage.OffsetReadUInt32(offset));
					value = peImage.OffsetReadUInt32(offset + 4);
				}
				else
					value = GetValue(value);

				// Seems there's a bug in their code where they sometimes overwrite valid data
				// with invalid data.
				if (startedPatchingBadData && value == 0x3115)
					continue;

				startedPatchingBadData |= !peImage.DotNetSafeWrite(rva, BitConverter.GetBytes(value));
			}

			return true;
		}

		static uint GetValue(uint value) {
			const uint magic = 2749;
			value = checked(value - magic);
			if (value % 3 != 0)
				throw new Exception();
			return value / 3;
		}
	}
}
