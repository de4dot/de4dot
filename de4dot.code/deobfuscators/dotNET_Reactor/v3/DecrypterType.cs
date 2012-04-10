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
using System.Collections.Generic;
using System.Text;
using Mono.Cecil;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	// Find the type that decrypts strings and calls the native lib
	class DecrypterType {
		ModuleDefinition module;
		TypeDefinition decrypterType;
		MethodDefinition stringDecrypter1;
		MethodDefinition stringDecrypter2;
		List<MethodDefinition> initMethods = new List<MethodDefinition>();
		List<ModuleReference> moduleReferences = new List<ModuleReference>();
		Resource linkedResource;

		public bool Detected {
			get { return decrypterType != null; }
		}

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public Resource LinkedResource {
			get { return linkedResource; }
		}

		public MethodDefinition StringDecrypter1 {
			get { return stringDecrypter1; }
		}

		public MethodDefinition StringDecrypter2 {
			get { return stringDecrypter2; }
		}

		public IEnumerable<MethodDefinition> InitMethods {
			get { return initMethods; }
		}

		public List<ModuleReference> ModuleReferences {
			get { return moduleReferences; }
		}

		public IEnumerable<MethodDefinition> StringDecrypters {
			get {
				return new List<MethodDefinition> {
					stringDecrypter1,
					stringDecrypter2,
				};
			}
		}

		public DecrypterType(ModuleDefinition module) {
			this.module = module;
		}

		public DecrypterType(ModuleDefinition module, DecrypterType oldOne) {
			this.module = module;
			this.decrypterType = lookup(oldOne.decrypterType, "Could not find decrypterType");
			this.stringDecrypter1 = lookup(oldOne.stringDecrypter1, "Could not find stringDecrypter1");
			this.stringDecrypter2 = lookup(oldOne.stringDecrypter2, "Could not find stringDecrypter2");
			foreach (var method in oldOne.initMethods)
				initMethods.Add(lookup(method, "Could not find initMethod"));
			updateModuleReferences();
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find() {
			foreach (var type in module.Types) {
				if (type.FullName != "<PrivateImplementationDetails>{B4838DC1-AC79-43d1-949F-41B518B904A8}")
					continue;

				decrypterType = type;
				stringDecrypter1 = getStringDecrypter(type, "CS$0$0004");
				stringDecrypter2 = getStringDecrypter(type, "CS$0$0005");
				foreach (var method in type.Methods) {
					if (DotNetUtils.isMethod(method, "System.Void", "()"))
						initMethods.Add(method);
				}
				updateModuleReferences();
				return;
			}
		}

		void updateModuleReferences() {
			foreach (var method in decrypterType.Methods) {
				if (method.PInvokeInfo != null) {
					switch (method.PInvokeInfo.EntryPoint) {
					case "nr_nli":
					case "nr_startup":
						moduleReferences.Add(method.PInvokeInfo.Module);
						break;
					}
				}
			}
			updateLinkedResource();
		}

		void updateLinkedResource() {
			foreach (var modref in moduleReferences) {
				var resource = DotNetUtils.getResource(module, modref.Name) as LinkedResource;
				if (resource == null)
					continue;

				linkedResource = resource;
				return;
			}
		}

		MethodDefinition getStringDecrypter(TypeDefinition type, string name) {
			var method = DotNetUtils.getMethod(type, name);
			if (method == null)
				return null;
			if (!DotNetUtils.isMethod(method, "System.String", "(System.String)"))
				return null;
			return method;
		}

		public string decrypt1(string s) {
			var sb = new StringBuilder(s.Length);
			foreach (var c in s)
				sb.Append((char)(0xFF - (byte)c));
			return sb.ToString();
		}

		public string decrypt2(string s) {
			return Encoding.Unicode.GetString(Convert.FromBase64String(s));
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

			bool startedPatchingBadData = false;
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

				// Seems there's a bug in their code where they sometimes overwrite valid data
				// with invalid data.
				if (startedPatchingBadData && value == 0x3115)
					continue;

				startedPatchingBadData |= !peImage.dotNetSafeWrite(rva, BitConverter.GetBytes(value));
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
