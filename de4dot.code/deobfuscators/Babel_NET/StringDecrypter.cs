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

using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class StringDecrypter {
		ModuleDefinition module;
		Dictionary<int, string> offsetToString = new Dictionary<int, string>();
		TypeDefinition decrypterType;
		MethodDefinition decryptMethod;
		EmbeddedResource encryptedResource;

		public bool Detected {
			get { return decrypterType != null; }
		}

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public MethodDefinition DecryptMethod {
			get { return decryptMethod; }
		}

		public EmbeddedResource Resource {
			get { return encryptedResource; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (!isDecrypterType(type))
					continue;

				var method = DotNetUtils.getMethod(type, "System.String", "(System.Int32)");
				if (method == null)
					continue;

				decrypterType = type;
				decryptMethod = method;
				return;
			}
		}

		bool isDecrypterType(TypeDefinition type) {
			if (type.HasEvents)
				return false;
			if (type.NestedTypes.Count != 1)
				return false;
			if (type.Fields.Count != 0)
				return false;

			var nested = type.NestedTypes[0];
			if (nested.HasProperties || nested.HasEvents)
				return false;

			if (nested.Fields.Count == 1) {
				// 4.2+ (maybe 4.0+)

				if (!MemberReferenceHelper.compareTypes(nested.Fields[0].FieldType, nested))
					return false;

				if (DotNetUtils.getMethod(nested, "System.Reflection.Emit.MethodBuilder", "(System.Reflection.Emit.TypeBuilder)") == null)
					return false;
			}
			else if (nested.Fields.Count == 2) {
				// 3.5 and maybe earlier

				var field1 = nested.Fields[0];
				var field2 = nested.Fields[1];
				if (field1.FieldType.FullName != "System.Collections.Hashtable" && field2.FieldType.FullName != "System.Collections.Hashtable")
					return false;
				if (!MemberReferenceHelper.compareTypes(field1.FieldType, nested) && !MemberReferenceHelper.compareTypes(field2.FieldType, nested))
					return false;
			}
			else
				return false;

			if (DotNetUtils.getMethod(nested, ".ctor") == null)
				return false;
			if (DotNetUtils.getMethod(nested, "System.String", "(System.Int32)") == null)
				return false;

			return true;
		}

		public void initialize() {
			if (decrypterType == null)
				return;
			if (encryptedResource != null)
				return;
			encryptedResource = findResource();
			if (encryptedResource == null)
				return;

			var decrypted = new ResourceDecrypter(module).decrypt(encryptedResource.GetResourceData());
			var reader = new BinaryReader(new MemoryStream(decrypted));
			while (reader.BaseStream.Position < reader.BaseStream.Length)
				offsetToString[(int)reader.BaseStream.Position] = reader.ReadString();
		}

		EmbeddedResource findResource() {
			foreach (var method in decrypterType.Methods) {
				foreach (var s in DotNetUtils.getCodeStrings(method)) {
					var resource = DotNetUtils.getResource(module, s) as EmbeddedResource;
					if (resource != null)
						return resource;
				}
			}
			return null;
		}

		public string decrypt(int offset) {
			return offsetToString[offset];
		}
	}
}
