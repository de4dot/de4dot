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
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeWall {
	class StringDecrypter {
		ModuleDefinition module;
		MethodDefinitionAndDeclaringTypeDict<StringEncrypterInfo> stringEncrypterInfos = new MethodDefinitionAndDeclaringTypeDict<StringEncrypterInfo>();
		Version version;

		public enum Version {
			Unknown,
			V30,	// 3.0 - 3.5
			V36,	// 3.6 - 4.1
		}

		public class StringEncrypterInfo {
			MethodDefinition method;

			public TypeDefinition Type {
				get { return method.DeclaringType; }
			}

			public MethodDefinition Method {
				get { return method; }
			}

			public EmbeddedResource Resource { get; set; }
			public int Magic1 { get; set; }
			public int Magic2 { get; set; }
			public int Magic3 { get; set; }
			public BinaryReader Reader { get; set; }

			public StringEncrypterInfo(MethodDefinition method) {
				this.method = method;
			}

			public string decrypt(int magic1, int magic2, int magic3) {
				int dataLen = magic3 ^ Magic3;
				var key = getKey(magic1 ^ Magic1, dataLen);
				Reader.BaseStream.Position = getDataOffset(magic2);
				var data = Reader.ReadBytes(dataLen);
				for (int i = 0; i < dataLen; i++)
					data[i] ^= key[i];
				return Encoding.Unicode.GetString(data);
			}

			byte[] getKey(int seed, int keyLen) {
				var random = new Random(seed);
				var key = new byte[keyLen];
				random.NextBytes(key);
				return key;
			}

			int getDataOffset(int magic2) {
				var pkt = getPublicKeyToken();
				if (pkt == null)
					return magic2 ^ Magic2;
				else
					return magic2 ^ BitConverter.ToInt32(pkt, 0) ^ BitConverter.ToInt32(pkt, 4);
			}

			byte[] getPublicKeyToken() {
				var module = method.Module;
				if (module.Assembly == null || module.Assembly.Name.PublicKeyToken == null)
					return null;
				if (module.Assembly.Name.PublicKeyToken.Length != 8)
					return null;
				return module.Assembly.Name.PublicKeyToken;
			}

			public override string ToString() {
				return string.Format("{0:X8} M1:{1:X8} M2:{2:X8} M3:{3:X8}",
						Method.MetadataToken.ToInt32(),
						Magic1, Magic2, Magic3);
			}
		}

		public bool Detected {
			get { return stringEncrypterInfos.Count != 0; }
		}

		public Version TheVersion {
			get { return version; }
		}

		public IEnumerable<StringEncrypterInfo> Infos {
			get {
				var list = new List<StringEncrypterInfo>();
				foreach (var info in stringEncrypterInfos.getValues()) {
					if (info.Resource != null)
						list.Add(info);
				}
				return list;
			}
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				MethodDefinition decrypterMethod;
				var decrypterVersion = checkType(type, out decrypterMethod);
				if (decrypterVersion == Version.Unknown)
					continue;
				version = decrypterVersion;
				stringEncrypterInfos.add(decrypterMethod, new StringEncrypterInfo(decrypterMethod));
			}
		}

		Version checkType(TypeDefinition type, out MethodDefinition decrypterMethod) {
			MethodDefinition method;

			if ((method = checkType_v30(type)) != null) {
				decrypterMethod = method;
				return Version.V30;
			}

			if ((method = checkType_v36(type)) != null) {
				decrypterMethod = method;
				return Version.V36;
			}

			decrypterMethod = null;
			return Version.Unknown;
		}

		static readonly string[] requiredTypes_v30 = new string[] {
			"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
		};
		static readonly string[] requiredLocals_v30 = new string[] {
			"System.Int32",
			"System.Byte[]",
			"System.Reflection.Assembly",
			"System.IO.Stream",
			"System.Random",
			"System.String",
		};
		MethodDefinition checkType_v30(TypeDefinition type) {
			MethodDefinition decrypterMethod = checkMethods_v30(type);
			if (decrypterMethod == null)
				return null;
			if (!new FieldTypes(type).exactly(requiredTypes_v30))
				return null;
			if (!new LocalTypes(decrypterMethod).exactly(requiredLocals_v30))
				return null;

			return decrypterMethod;
		}

		static MethodDefinition checkMethods_v30(TypeDefinition type) {
			if (type.Methods.Count < 1 || type.Methods.Count > 2)
				return null;

			MethodDefinition decrypterMethod = null;
			MethodDefinition cctor = null;
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor") {
					cctor = method;
					continue;
				}
				if (decrypterMethod != null)
					return null;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.Int32,System.Int32,System.Int32)"))
					return null;
				decrypterMethod = method;
			}
			if (decrypterMethod == null || !decrypterMethod.IsStatic)
				return null;
			return decrypterMethod;
		}

		static readonly string[] requiredTypes_v36 = new string[] {
			"System.Object",
			"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
		};
		static readonly string[] requiredLocals_v36 = new string[] {
			"System.Int32",
			"System.Byte[]",
			"System.Reflection.Assembly",
			"System.IO.Stream",
			"System.Random",
			"System.String",
			"System.Object",
		};
		MethodDefinition checkType_v36(TypeDefinition type) {
			MethodDefinition decrypterMethod = checkMethods_v36(type);
			if (decrypterMethod == null)
				return null;
			if (!new FieldTypes(type).exactly(requiredTypes_v36))
				return null;
			if (!new LocalTypes(decrypterMethod).exactly(requiredLocals_v36))
				return null;

			return decrypterMethod;
		}

		static MethodDefinition checkMethods_v36(TypeDefinition type) {
			if (type.Methods.Count != 2)
				return null;

			MethodDefinition decrypterMethod = null;
			MethodDefinition cctor = null;
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor") {
					cctor = method;
					continue;
				}
				if (decrypterMethod != null)
					return null;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.Int32,System.Int32,System.Int32)"))
					return null;
				decrypterMethod = method;
			}
			if (cctor == null)
				return null;
			if (decrypterMethod == null || !decrypterMethod.IsStatic)
				return null;
			return decrypterMethod;
		}

		public void initialize(ISimpleDeobfuscator simpleDeobfuscator) {
			foreach (var info in stringEncrypterInfos.getValues()) {
				simpleDeobfuscator.deobfuscate(info.Method);
				info.Resource = findResource(info.Method);
				if (info.Resource == null) {
					Log.w("Could not find encrypted strings resource (Method {0:X8})", info.Method.MetadataToken.ToInt32());
					continue;
				}
				info.Magic1 = findMagic1(info.Method);
				info.Magic2 = findMagic2(info.Method);
				info.Magic3 = findMagic3(info.Method);
				info.Reader = new BinaryReader(info.Resource.GetResourceStream());
			}
		}

		EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		static int findMagic1(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldarg = instrs[i];
				if (!DotNetUtils.isLdarg(ldarg) || DotNetUtils.getArgIndex(ldarg) != 0)
					continue;
				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				return DotNetUtils.getLdcI4Value(ldci4);
			}
			throw new ApplicationException("Could not find magic1");
		}

		static int findMagic2(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldloc = instrs[i];
				if (!DotNetUtils.isLdloc(ldloc))
					continue;
				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				return DotNetUtils.getLdcI4Value(ldci4);
			}
			throw new ApplicationException("Could not find magic2");
		}

		static int findMagic3(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldarg = instrs[i];
				if (!DotNetUtils.isLdarg(ldarg) || DotNetUtils.getArgIndex(ldarg) != 2)
					continue;
				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				return DotNetUtils.getLdcI4Value(ldci4);
			}
			throw new ApplicationException("Could not find magic3");
		}

		public string decrypt(MethodDefinition method, int magic1, int magic2, int magic3) {
			var info = stringEncrypterInfos.find(method);
			return info.decrypt(magic1, magic2, magic3);
		}
	}
}
