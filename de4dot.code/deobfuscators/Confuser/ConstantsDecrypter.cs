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
using Mono.Cecil.Metadata;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.Confuser {
	class ConstantsDecrypter {
		ModuleDefinition module;
		byte[] fileData;
		ISimpleDeobfuscator simpleDeobfuscator;
		FieldDefinition dictField, dataField;
		MethodDefinition installMethod;
		MethodDefinitionAndDeclaringTypeDict<DecrypterInfo> decrypters = new MethodDefinitionAndDeclaringTypeDict<DecrypterInfo>();
		uint key0, key0d;
		MethodDefinition nativeMethod;
		EmbeddedResource resource;
		byte[] constants;
		ConstType constType = ConstType.Unknown;

		enum ConstType {
			Unknown,
			Normal,		// type is not dynamic and native
			Dynamic,	// type="dynamic"
			Native,		// type="native"
		}

		public class DecrypterInfo {
			readonly ConstantsDecrypter constantsDecrypter;
			public readonly MethodDefinition method;
			public ulong key0l, key1l, key2l;
			public uint key0, key0d;

			public DecrypterInfo(ConstantsDecrypter constantsDecrypter, MethodDefinition method) {
				this.constantsDecrypter = constantsDecrypter;
				this.method = method;
			}

			public string decryptString(uint magic1, ulong magic2) {
				return Encoding.UTF8.GetString(decrypt(magic1, magic2));
			}

			public int decryptInt32(uint magic1, ulong magic2) {
				return BitConverter.ToInt32(decrypt(magic1, magic2), 0);
			}

			public long decryptInt64(uint magic1, ulong magic2) {
				return BitConverter.ToInt64(decrypt(magic1, magic2), 0);
			}

			public float decryptSingle(uint magic1, ulong magic2) {
				return BitConverter.ToSingle(decrypt(magic1, magic2), 0);
			}

			public double decryptDouble(uint magic1, ulong magic2) {
				return BitConverter.ToDouble(decrypt(magic1, magic2), 0);
			}

			byte[] decrypt(uint magic1, ulong magic2) {
				ulong info = hash(method.DeclaringType.MetadataToken.ToUInt32() * magic1) ^ magic2;
				int offset = (int)(info >> 32);
				int len = (int)info;
				var decrypted = new byte[len];
				byte[] key = BitConverter.GetBytes(method.MetadataToken.ToUInt32() ^ key0d);
				for (int i = 0; i < len; i++)
					decrypted[i] = (byte)(constantsDecrypter.constants[offset + i] ^ key[(offset + i) & 3]);
				return decrypted;
			}

			ulong hash(uint magic) {
				ulong h0 = key0l * magic;
				ulong h1 = key1l;
				ulong h2 = key2l;
				h1 *= h0;
				h2 *= h0;
				h0 *= h0;
				ulong hash = 14695981039346656037UL;
				while (h0 != 0) {
					hash *= 1099511628211UL;
					hash = (hash ^ h0) + (h1 ^ h2) * key0;
					h1 *= 0x811C9DC5;
					h2 *= 0xA2CEBAB2;
					h0 >>= 8;
				}
				return hash;
			}
		}

		public IEnumerable<TypeDefinition> Types {
			get {
				var types = new List<TypeDefinition>();
				foreach (var info in decrypters.getValues())
					types.Add(info.method.DeclaringType);
				return types;
			}
		}

		public IEnumerable<FieldDefinition> Fields {
			get {
				return new List<FieldDefinition> {
					dataField,
					dictField,
				};
			}
		}

		public MethodDefinition NativeMethod {
			get { return nativeMethod; }
		}

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public IEnumerable<DecrypterInfo> Decrypters {
			get { return decrypters.getValues(); }
		}

		public bool Detected {
			get { return installMethod != null; }
		}

		public ConstantsDecrypter(ModuleDefinition module, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.fileData = fileData;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public void find() {
			var cctor = DotNetUtils.getModuleTypeCctor(module);
			if (cctor == null)
				return;
			simpleDeobfuscator.deobfuscate(cctor, true);

			if ((dictField = findDictField(cctor, cctor.DeclaringType)) == null)
				return;
			if ((dataField = findDataField(cctor, cctor.DeclaringType)) == null)
				return;

			installMethod = cctor;
		}

		static FieldDefinition findDictField(MethodDefinition method, TypeDefinition declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var newobj = instrs[i];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;
				var ctor = newobj.Operand as MethodReference;
				if (ctor == null || ctor.FullName != "System.Void System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>::.ctor()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDefinition;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != "System.Collections.Generic.Dictionary`2<System.UInt32,System.Object>")
					continue;

				return field;
			}
			return null;
		}

		static FieldDefinition findDataField(MethodDefinition method, TypeDefinition declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var callvirt = instrs[i];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;
				var ctor = callvirt.Operand as MethodReference;
				if (ctor == null || ctor.FullName != "System.Byte[] System.IO.MemoryStream::ToArray()")
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDefinition;
				if (field == null || field.DeclaringType != declaringType)
					continue;
				if (field.FieldType.FullName != "System.Byte[]")
					continue;

				return field;
			}
			return null;
		}

		public void initialize() {
			if (installMethod == null)
				return;

			if (!findKeys())
				throw new ApplicationException("Could not find keys");
			nativeMethod = findNativeMethod(installMethod, installMethod.DeclaringType);

			constType = detectConstType();
			if (constType == ConstType.Unknown)
				throw new ApplicationException("Could not detect const type");

			if ((resource = findResource(key0)) == null)
				throw new ApplicationException("Could not find resource");
			constants = decrypt(resource.GetResourceData());

			findDecrypters();
		}

		ConstType detectConstType() {
			if (nativeMethod != null)
				return ConstType.Native;
			else if (DeobUtils.hasInteger(installMethod, 0x10000))
				return ConstType.Normal;
			else
				return ConstType.Dynamic;
		}

		EmbeddedResource findResource(uint magic) {
			var name = Encoding.UTF8.GetString(BitConverter.GetBytes(magic));
			return DotNetUtils.getResource(module, name) as EmbeddedResource;
		}

		bool findKeys() {
			if (!findKey0(installMethod, out key0))
				return false;
			if (!findKey0d(installMethod, out key0d))
				return false;

			return true;
		}

		static bool findKey0(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Call, "System.Text.Encoding System.Text.Encoding::get_UTF8()");
				if (index < 0)
					break;
				int index2 = ConfuserUtils.findCallMethod(instrs, i, Code.Call, "System.Byte[] System.BitConverter::GetBytes(System.Int32)");
				if (index2 - index != 2)
					continue;
				var ldci4 = instrs[index + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}

			key = 0;
			return false;
		}

		static bool findKey0d(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Reflection.Module System.Reflection.MemberInfo::get_Module()");
				if (index < 0)
					break;
				int index2 = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()");
				if (index2 - index != 3)
					continue;
				var ldci4 = instrs[index + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (!DotNetUtils.isLdloc(instrs[index + 2]))
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}

			key = 0;
			return false;
		}

		static MethodDefinition findNativeMethod(MethodDefinition method, TypeDefinition declaringType) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				if (!DotNetUtils.isLdloc(instrs[i]))
					continue;
				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null || !calledMethod.IsStatic || !calledMethod.IsNative)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Int32", "(System.Int32)"))
					continue;

				return calledMethod;
			}
			return null;
		}

		void findDecrypters() {
			foreach (var type in module.Types) {
				foreach (var method in type.Methods) {
					var info = createDecrypterInfo(method);
					if (info != null)
						decrypters.add(info.method, info);
				}
			}
		}

		DecrypterInfo createDecrypterInfo(MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;
			if (!method.IsStatic)
				return null;
			if (method.Parameters.Count != 2)
				return null;
			if (method.Parameters[0].ParameterType.EType != ElementType.U4)
				return null;
			if (method.Parameters[1].ParameterType.EType != ElementType.U8)
				return null;
			if (!(method.MethodReturnType.ReturnType is GenericParameter))
				return null;
			if (method.GenericParameters.Count != 1)
				return null;

			simpleDeobfuscator.deobfuscate(method);
			var info = new DecrypterInfo(this, method);
			if (!findLKeys(info))
				return null;
			if (!findKey0(info))
				return null;
			if (!findKey0d(info))
				return null;

			return info;
		}

		static bool findLKeys(DecrypterInfo info) {
			var instrs = info.method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 8; i++) {
				var ldci8_1 = instrs[i];
				if (ldci8_1.OpCode.Code != Code.Ldc_I8)
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 1]))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Conv_U8)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Mul)
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 4]))
					continue;
				var ldci8_2 = instrs[i + 5];
				if (ldci8_2.OpCode.Code != Code.Ldc_I8)
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 6]))
					continue;
				var ldci8_3 = instrs[i + 7];
				if (ldci8_3.OpCode.Code != Code.Ldc_I8)
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 8]))
					continue;

				info.key0l = (ulong)(long)ldci8_1.Operand;
				info.key1l = (ulong)(long)ldci8_2.Operand;
				info.key2l = (ulong)(long)ldci8_3.Operand;
				return true;
			}
			return false;
		}

		static bool findKey0(DecrypterInfo info) {
			var instrs = info.method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				if (!DotNetUtils.isLdloc(instrs[i]))
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Conv_U8)
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Mul)
					continue;

				info.key0 = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			return false;
		}

		static bool findKey0d(DecrypterInfo info) {
			var instrs = info.method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Int32 System.Reflection.MemberInfo::get_MetadataToken()");
				if (index < 0)
					break;
				int index2 = ConfuserUtils.findCallMethod(instrs, index, Code.Call, "System.Byte[] System.BitConverter::GetBytes(System.Int32)");
				if (index2 < 0)
					break;
				if (index2 - index != 3)
					continue;
				var ldci4 = instrs[index + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[index + 2].OpCode.Code != Code.Xor)
					continue;

				info.key0d = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			return false;
		}

		byte[] decrypt(byte[] encrypted) {
			switch (constType) {
			case ConstType.Normal: return decryptNormal(encrypted);
			case ConstType.Dynamic: return decryptDynamic(encrypted);
			case ConstType.Native: return decryptNative(encrypted);
			default: throw new ApplicationException(string.Format("Unknown const type: {0}", constType));
			}
		}

		byte[] getSigKey() {
			uint sigToken = key0d ^ installMethod.MetadataToken.ToUInt32();
			if ((sigToken & 0xFF000000) != 0x11000000)
				throw new ApplicationException("Invalid sig token");
			return module.GetSignatureBlob(sigToken);
		}

		byte[] decryptNormal(byte[] encrypted) {
			var key = getSigKey();

			var decrypted = new byte[encrypted.Length];
			uint seed = BitConverter.ToUInt32(key, 12) * (uint)key0;
			ushort _m = (ushort)(seed >> 16);
			ushort _c = (ushort)seed;
			ushort m = _c; ushort c = _m;
			for (int i = 0; i < decrypted.Length; i++) {
				decrypted[i] = (byte)(encrypted[i] ^ ((seed * m + c) & 0xFF));
				m = (ushort)(seed * m + _m);
				c = (ushort)(seed * c + _c);
			}

			return DeobUtils.inflate(DeobUtils.aesDecrypt(decrypted, key, DeobUtils.md5Sum(key)), true);
		}

		byte[] decryptDynamic(byte[] encrypted) {
			throw new NotSupportedException();	//TODO:
		}

		byte[] decryptNative(byte[] encrypted) {
			var key = getSigKey();

			var decrypted = DeobUtils.aesDecrypt(encrypted, key, DeobUtils.md5Sum(key));
			decrypted = DeobUtils.inflate(decrypted, true);

			var x86Emu = new x86Emulator(new PeImage(fileData));
			var reader = new BinaryReader(new MemoryStream(decrypted));
			var result = new MemoryStream();
			var writer = new BinaryWriter(result);
			while (reader.BaseStream.Position < reader.BaseStream.Length) {
				uint magic = Utils.readEncodedUInt32(reader);
				writer.Write((byte)x86Emu.emulate((uint)nativeMethod.RVA, magic));
			}

			return result.ToArray();
		}

		static bool verifyGenericArg(GenericInstanceMethod gim, ElementType etype) {
			if (gim == null || gim.GenericArguments.Count != 1)
				return false;
			return gim.GenericArguments[0].EType == etype;
		}

		public string decryptString(MethodDefinition method, GenericInstanceMethod gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.String))
				return null;
			var info = decrypters.find(method);
			return info.decryptString(magic1, magic2);
		}

		public object decryptInt32(MethodDefinition method, GenericInstanceMethod gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.I4))
				return null;
			var info = decrypters.find(method);
			return info.decryptInt32(magic1, magic2);
		}

		public object decryptInt64(MethodDefinition method, GenericInstanceMethod gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.I8))
				return null;
			var info = decrypters.find(method);
			return info.decryptInt64(magic1, magic2);
		}

		public object decryptSingle(MethodDefinition method, GenericInstanceMethod gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.R4))
				return null;
			var info = decrypters.find(method);
			return info.decryptSingle(magic1, magic2);
		}

		public object decryptDouble(MethodDefinition method, GenericInstanceMethod gim, uint magic1, ulong magic2) {
			if (!verifyGenericArg(gim, ElementType.R8))
				return null;
			var info = decrypters.find(method);
			return info.decryptDouble(magic1, magic2);
		}

		public void cleanUp() {
			if (installMethod == null)
				return;

			//TODO: Only remove its code
			installMethod.Body.Instructions.Clear();
			installMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
		}
	}
}
