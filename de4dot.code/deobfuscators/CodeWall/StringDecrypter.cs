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
using System.IO;
using System.Text;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeWall {
	class StringDecrypter {
		ModuleDefMD module;
		MethodDefAndDeclaringTypeDict<StringEncrypterInfo> stringEncrypterInfos = new MethodDefAndDeclaringTypeDict<StringEncrypterInfo>();
		Version version;

		public enum Version {
			Unknown,
			V30,	// 3.0 - 3.5
			V36,	// 3.6 - 4.1
		}

		public class StringEncrypterInfo {
			MethodDef method;

			public TypeDef Type {
				get { return method.DeclaringType; }
			}

			public MethodDef Method {
				get { return method; }
			}

			public EmbeddedResource Resource { get; set; }
			public int Magic1 { get; set; }
			public int Magic2 { get; set; }
			public int Magic3 { get; set; }
			public IBinaryReader Reader { get; set; }

			public StringEncrypterInfo(MethodDef method) {
				this.method = method;
			}

			public string Decrypt(int magic1, int magic2, int magic3) {
				int dataLen = magic3 ^ Magic3;
				var key = GetKey(magic1 ^ Magic1, dataLen);
				Reader.Position = GetDataOffset(magic2);
				var data = Reader.ReadBytes(dataLen);
				for (int i = 0; i < dataLen; i++)
					data[i] ^= key[i];
				return Encoding.Unicode.GetString(data);
			}

			byte[] GetKey(int seed, int keyLen) {
				var random = new Random(seed);
				var key = new byte[keyLen];
				random.NextBytes(key);
				return key;
			}

			int GetDataOffset(int magic2) {
				var pkt = GetPublicKeyToken();
				if (pkt == null)
					return magic2 ^ Magic2;
				else
					return magic2 ^ BitConverter.ToInt32(pkt, 0) ^ BitConverter.ToInt32(pkt, 4);
			}

			byte[] GetPublicKeyToken() {
				var module = method.Module;
				if (module.Assembly == null || PublicKeyBase.IsNullOrEmpty2(module.Assembly.PublicKey))
					return null;
				return module.Assembly.PublicKeyToken.Data;
			}

			public override string ToString() {
				return string.Format("{0:X8} M1:{1:X8} M2:{2:X8} M3:{3:X8}",
						Method.MDToken.ToInt32(),
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
				foreach (var info in stringEncrypterInfos.GetValues()) {
					if (info.Resource != null)
						list.Add(info);
				}
				return list;
			}
		}

		public StringDecrypter(ModuleDefMD module) {
			this.module = module;
		}

		public void Find() {
			foreach (var type in module.Types) {
				MethodDef decrypterMethod;
				var decrypterVersion = CheckType(type, out decrypterMethod);
				if (decrypterVersion == Version.Unknown)
					continue;
				version = decrypterVersion;
				stringEncrypterInfos.Add(decrypterMethod, new StringEncrypterInfo(decrypterMethod));
			}
		}

		Version CheckType(TypeDef type, out MethodDef decrypterMethod) {
			MethodDef method;

			if ((method = CheckTypeV30(type)) != null) {
				decrypterMethod = method;
				return Version.V30;
			}

			if ((method = CheckTypeV36(type)) != null) {
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
		MethodDef CheckTypeV30(TypeDef type) {
			MethodDef decrypterMethod = CheckMethodsV30(type);
			if (decrypterMethod == null)
				return null;
			if (!new FieldTypes(type).Exactly(requiredTypes_v30))
				return null;
			if (!new LocalTypes(decrypterMethod).Exactly(requiredLocals_v30))
				return null;

			return decrypterMethod;
		}

		static MethodDef CheckMethodsV30(TypeDef type) {
			if (type.Methods.Count < 1 || type.Methods.Count > 2)
				return null;

			MethodDef decrypterMethod = null;
			//MethodDef cctor = null;
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor") {
					//cctor = method;
					continue;
				}
				if (decrypterMethod != null)
					return null;
				if (!DotNetUtils.IsMethod(method, "System.String", "(System.Int32,System.Int32,System.Int32)"))
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
		MethodDef CheckTypeV36(TypeDef type) {
			MethodDef decrypterMethod = CheckMethodsV36(type);
			if (decrypterMethod == null)
				return null;
			if (!new FieldTypes(type).Exactly(requiredTypes_v36))
				return null;
			if (!new LocalTypes(decrypterMethod).Exactly(requiredLocals_v36))
				return null;

			return decrypterMethod;
		}

		static MethodDef CheckMethodsV36(TypeDef type) {
			if (type.Methods.Count != 2)
				return null;

			MethodDef decrypterMethod = null;
			MethodDef cctor = null;
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor") {
					cctor = method;
					continue;
				}
				if (decrypterMethod != null)
					return null;
				if (!DotNetUtils.IsMethod(method, "System.String", "(System.Int32,System.Int32,System.Int32)"))
					return null;
				decrypterMethod = method;
			}
			if (cctor == null)
				return null;
			if (decrypterMethod == null || !decrypterMethod.IsStatic)
				return null;
			return decrypterMethod;
		}

		public void Initialize(ISimpleDeobfuscator simpleDeobfuscator) {
			foreach (var info in stringEncrypterInfos.GetValues()) {
				simpleDeobfuscator.Deobfuscate(info.Method);
				info.Resource = FindResource(info.Method);
				if (info.Resource == null) {
					Logger.w("Could not find encrypted strings resource (Method {0:X8})", info.Method.MDToken.ToInt32());
					continue;
				}
				info.Magic1 = FindMagic1(info.Method);
				info.Magic2 = FindMagic2(info.Method);
				info.Magic3 = FindMagic3(info.Method);
				info.Reader = info.Resource.Data;
				info.Reader.Position = 0;
			}
		}

		EmbeddedResource FindResource(MethodDef method) {
			return DotNetUtils.GetResource(module, DotNetUtils.GetCodeStrings(method)) as EmbeddedResource;
		}

		static int FindMagic1(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldarg = instrs[i];
				if (!ldarg.IsLdarg() || ldarg.GetParameterIndex() != 0)
					continue;
				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				return ldci4.GetLdcI4Value();
			}
			throw new ApplicationException("Could not find magic1");
		}

		static int FindMagic2(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldloc = instrs[i];
				if (!ldloc.IsLdloc())
					continue;
				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				return ldci4.GetLdcI4Value();
			}
			throw new ApplicationException("Could not find magic2");
		}

		static int FindMagic3(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldarg = instrs[i];
				if (!ldarg.IsLdarg() || ldarg.GetParameterIndex() != 2)
					continue;
				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				return ldci4.GetLdcI4Value();
			}
			throw new ApplicationException("Could not find magic3");
		}

		public string Decrypt(MethodDef method, int magic1, int magic2, int magic3) {
			var info = stringEncrypterInfos.Find(method);
			return info.Decrypt(magic1, magic2, magic3);
		}
	}
}
