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
using de4dot.PE;

namespace de4dot.code.deobfuscators.Confuser {
	class ProxyCallFixer : ProxyCallFixer2, IVersionProvider {
		MethodDefinitionAndDeclaringTypeDict<ProxyCreatorInfo> methodToInfo = new MethodDefinitionAndDeclaringTypeDict<ProxyCreatorInfo>();
		FieldDefinitionAndDeclaringTypeDict<List<MethodDefinition>> fieldToMethods = new FieldDefinitionAndDeclaringTypeDict<List<MethodDefinition>>();
		string ourAsm;
		ConfuserVersion version = ConfuserVersion.Unknown;
		byte[] fileData;
		x86Emulator x86emu;
		ushort callvirtChar;

		enum ConfuserVersion {
			Unknown,
			v10_r42915,
			v10_r42919,
			v10_r48717,
			v11_r50378,
			v12_r54564,
			v14_r58564,
			v14_r58857,
			v17_r73740_normal,
			v17_r73740_native,
			v17_r74708_normal,
			v17_r74708_native,
			v18_r75367_normal,
			v18_r75367_native,
			v19_r76101_normal,
			v19_r76101_native,
		}

		enum ProxyCreatorType {
			None,
			CallOrCallvirt,
			Newobj,
		}

		class ProxyCreatorInfo {
			public readonly MethodDefinition creatorMethod;
			public readonly ProxyCreatorType proxyCreatorType;
			public readonly ConfuserVersion version;
			public readonly uint magic;
			public readonly MethodDefinition nativeMethod;
			public readonly ushort callvirtChar;

			public ProxyCreatorInfo(MethodDefinition creatorMethod, ProxyCreatorType proxyCreatorType, ConfuserVersion version, uint magic, MethodDefinition nativeMethod, ushort callvirtChar) {
				this.creatorMethod = creatorMethod;
				this.proxyCreatorType = proxyCreatorType;
				this.version = version;
				this.magic = magic;
				this.nativeMethod = nativeMethod;
				this.callvirtChar = callvirtChar;
			}
		}

		class DelegateInitInfo {
			public readonly byte[] data;
			public readonly FieldDefinition field;
			public readonly MethodDefinition creatorMethod;

			public DelegateInitInfo(FieldDefinition field, MethodDefinition creatorMethod) {
				this.field = field;
				this.creatorMethod = creatorMethod;
			}

			public DelegateInitInfo(string data, FieldDefinition field, MethodDefinition creatorMethod) {
				this.data = Convert.FromBase64String(data);
				this.field = field;
				this.creatorMethod = creatorMethod;
			}
		}

		protected override bool ProxyCallIsObfuscated {
			get { return true; }
		}

		public IEnumerable<FieldDefinition> Fields {
			get {
				var fields = new List<FieldDefinition>(fieldToMethods.getKeys());
				var type = DotNetUtils.getModuleType(module);
				if (fields.Count > 0 && type != null) {
					foreach (var field in type.Fields) {
						var fieldType = field.FieldType as TypeDefinition;
						if (fieldType != null && delegateTypesDict.ContainsKey(fieldType))
							fields.Add(field);
					}
				}
				return fields;
			}
		}

		public override IEnumerable<Tuple<MethodDefinition, string>> OtherMethods {
			get {
				var list = new List<Tuple<MethodDefinition, string>>();
				foreach (var info in methodToInfo.getValues()) {
					list.Add(new Tuple<MethodDefinition, string> {
						Item1 = info.creatorMethod,
						Item2 = "Delegate creator method",
					});
					list.Add(new Tuple<MethodDefinition, string> {
						Item1 = info.nativeMethod,
						Item2 = "Calculate RID native method",
					});
				}
				foreach (var methods in fieldToMethods.getValues()) {
					foreach (var method in methods) {
						list.Add(new Tuple<MethodDefinition, string> {
							Item1 = method,
							Item2 = "Proxy delegate method",
						});
					}
				}
				return list;
			}
		}

		public ProxyCallFixer(ModuleDefinition module, byte[] fileData)
			: base(module) {
			this.fileData = fileData;
			if (module.Assembly == null || module.Assembly.Name == null)
				ourAsm = new AssemblyNameReference(" -1-1-1-1-1- ", new Version(1, 2, 3, 4)).FullName;
			else
				ourAsm = module.Assembly.FullName;
		}

		protected override object checkCctor(TypeDefinition type, MethodDefinition cctor) {
			// Here if 1.2 r54564 (almost 1.3) or later

			var fieldToInfo = new FieldDefinitionAndDeclaringTypeDict<DelegateInitInfo>();

			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldtoken = instrs[i];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDefinition;
				if (field == null || field.DeclaringType != cctor.DeclaringType)
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				if (!isDelegateCreatorMethod(calledMethod))
					continue;
				var info = methodToInfo.find(calledMethod);
				if (info == null)
					continue;

				i++;
				fieldToInfo.add(field, new DelegateInitInfo(field, calledMethod));
			}
			return fieldToInfo.Count == 0 ? null : fieldToInfo;
		}

		protected override void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode) {
			var info = context as DelegateInitInfo;
			if (info == null) {
				var fieldToInfo = context as FieldDefinitionAndDeclaringTypeDict<DelegateInitInfo>;
				if (fieldToInfo != null)
					info = fieldToInfo.find(field);
			}
			if (info == null)
				throw new ApplicationException("Couldn't get the delegate info");
			var creatorInfo = methodToInfo.find(info.creatorMethod);

			switch (creatorInfo.version) {
			case ConfuserVersion.v10_r42915:
			case ConfuserVersion.v10_r42919:
				getCallInfo_v10_r42915(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v10_r48717:
			case ConfuserVersion.v11_r50378:
			case ConfuserVersion.v12_r54564:
			case ConfuserVersion.v14_r58564:
				getCallInfo_v10_r48717(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v14_r58857:
				getCallInfo_v14_r58857(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v17_r73740_normal:
			case ConfuserVersion.v17_r74708_normal:
				getCallInfo_v17_r73740_normal(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v17_r73740_native:
			case ConfuserVersion.v17_r74708_native:
				getCallInfo_v17_r73740_native(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v18_r75367_normal:
			case ConfuserVersion.v19_r76101_normal:
				getCallInfo_v18_r75367_normal(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v18_r75367_native:
			case ConfuserVersion.v19_r76101_native:
				getCallInfo_v18_r75367_native(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			default:
				throw new ApplicationException("Unknown version");
			}

			if (calledMethod == null) {
				Log.w("Could not find real method. Proxy field: {0:X8}", info.field.MetadataToken.ToInt32());
				errors++;
			}
		}

		void getCallInfo_v10_r42915(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out MethodReference calledMethod, out OpCode callOpcode) {
			var reader = new BinaryReader(new MemoryStream(info.data));

			bool isCallvirt = false;
			if (creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt)
				isCallvirt = reader.ReadBoolean();

			var asmRef = readAssemblyNameReference(reader);
			// If < 1.0 r42919, then high byte is 06, else it's cleared.
			uint token = (reader.ReadUInt32() & 0x00FFFFFF) | 0x06000000;
			if (reader.BaseStream.Position != reader.BaseStream.Length)
				throw new ApplicationException("Extra data");

			if (asmRef.FullName == ourAsm)
				calledMethod = (MethodReference)module.LookupToken((int)token);
			else
				calledMethod = createMethodReference(asmRef, token);

			callOpcode = getCallOpCode(creatorInfo, isCallvirt);
		}

		void getCallInfo_v10_r48717(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out MethodReference calledMethod, out OpCode callOpcode) {
			bool? isNew = isNewFieldNameEncoding(info);
			if (isNew == null) {
				calledMethod = null;
				callOpcode = OpCodes.Call;
				return;
			}

			int offs = creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt ? 2 : 1;
			if (isNew.Value)
				offs--;
			int callvirtOffs = isNew.Value ? 0 : 1;

			uint token = BitConverter.ToUInt32(Encoding.Unicode.GetBytes(info.field.Name.ToCharArray(), offs, 2), 0) ^ creatorInfo.magic;
			uint table = token >> 24;
			if (table != 0 && table != 6 && table != 0x0A && table != 0x2B)
				throw new ApplicationException("Invalid method token");

			// 1.3 r55346 now correctly uses method reference tokens and finally fixed the old
			// bug of using methoddef tokens to reference external methods.
			if (isNew.Value || info.field.Name[0] == (char)1 || table != 0x06)
				calledMethod = (MethodReference)module.LookupToken((int)token);
			else {
				var asmRef = module.AssemblyReferences[info.field.Name[0] - 2];
				calledMethod = createMethodReference(asmRef, token);
			}

			bool isCallvirt = false;
			if (creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt && info.field.Name[callvirtOffs] == '\r')
				isCallvirt = true;
			callOpcode = getCallOpCode(creatorInfo, isCallvirt);
		}

		// Returns true if Confuser 1.4 r58802 or later
		bool? isNewFieldNameEncoding(DelegateInitInfo info) {
			var creatorInfo = methodToInfo.find(info.creatorMethod);
			int oldLen, newLen;
			switch (creatorInfo.proxyCreatorType) {
			case ProxyCreatorType.Newobj:
				oldLen = 3;
				newLen = 2;
				break;

			case ProxyCreatorType.CallOrCallvirt:
				oldLen = 4;
				newLen = 3;
				break;

			default: throw new ApplicationException("Invalid proxy creator type");
			}

			if (info.field.Name.Length == oldLen)
				return false;
			if (info.field.Name.Length != newLen) {
				// This is an obfuscator bug. Field names are stored in the #Strings heap,
				// and strings in that heap are UTF8 zero terminated strings, but Confuser
				// can generate names with zeros in them. This was fixed in 1.4 58857.
				return null;
			}
			return true;
		}

		void getCallInfo_v14_r58857(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out MethodReference calledMethod, out OpCode callOpcode) {
			int offs = creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt ? 1 : 0;
			var nameInfo = decryptFieldName(info.field.Name);

			uint token = BitConverter.ToUInt32(nameInfo, offs) ^ creatorInfo.magic;
			uint table = token >> 24;
			if (table != 6 && table != 0x0A && table != 0x2B)
				throw new ApplicationException("Invalid method token");

			calledMethod = (MethodReference)module.LookupToken((int)token);

			bool isCallvirt = false;
			if (creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt && nameInfo[0] == '\r')
				isCallvirt = true;
			callOpcode = getCallOpCode(creatorInfo, isCallvirt);
		}

		static byte[] decryptFieldName(string name) {
			var chars = new char[name.Length];
			for (int i = 0; i < chars.Length; i++)
				chars[i] = (char)((byte)name[i] ^ i);
			return Convert.FromBase64CharArray(chars, 0, chars.Length);
		}

		void extract_v17_r73740(ProxyCreatorInfo creatorInfo, byte[] nameInfo, out uint arg, out uint table, out bool isCallvirt) {
			switch (creatorInfo.proxyCreatorType) {
			case ProxyCreatorType.CallOrCallvirt:
				arg = BitConverter.ToUInt32(nameInfo, 1);
				table = (uint)(nameInfo[0] & 0x7F) << 24;
				isCallvirt = (nameInfo[0] & 0x80) != 0;
				break;

			case ProxyCreatorType.Newobj:
				arg = BitConverter.ToUInt32(nameInfo, 0);
				table = (uint)nameInfo[4] << 24;
				isCallvirt = false;
				break;

			default:
				throw new ApplicationException("Invalid creator type");
			}
		}

		void getCallInfo_v17_r73740_normal(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out MethodReference calledMethod, out OpCode callOpcode) {
			var nameInfo = decryptFieldName(info.field.Name);
			uint arg, table;
			bool isCallvirt;
			extract_v17_r73740(creatorInfo, nameInfo, out arg, out table, out isCallvirt);
			uint token = (arg ^ creatorInfo.magic) | table;

			calledMethod = module.LookupToken((int)token) as MethodReference;
			callOpcode = getCallOpCode(creatorInfo, isCallvirt);
		}

		void getCallInfo_v17_r73740_native(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out MethodReference calledMethod, out OpCode callOpcode) {
			var nameInfo = decryptFieldName(info.field.Name);
			uint arg, table;
			bool isCallvirt;
			extract_v17_r73740(creatorInfo, nameInfo, out arg, out table, out isCallvirt);
			if (x86emu == null)
				x86emu = new x86Emulator(new PeImage(fileData));
			uint token = x86emu.emulate((uint)creatorInfo.nativeMethod.RVA, arg) | table;

			calledMethod = module.LookupToken((int)token) as MethodReference;
			callOpcode = getCallOpCode(creatorInfo, isCallvirt);
		}

		void getCallInfo_v18_r75367_normal(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out MethodReference calledMethod, out OpCode callOpcode) {
			getCallInfo_v18_r75367(info, creatorInfo, out calledMethod, out callOpcode, (creatorInfo2, magic) => creatorInfo2.magic ^ magic);
		}

		void getCallInfo_v18_r75367_native(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out MethodReference calledMethod, out OpCode callOpcode) {
			getCallInfo_v18_r75367(info, creatorInfo, out calledMethod, out callOpcode, (creatorInfo2, magic) => {
				if (x86emu == null)
					x86emu = new x86Emulator(new PeImage(fileData));
				return x86emu.emulate((uint)creatorInfo2.nativeMethod.RVA, magic);
			});
		}

		void getCallInfo_v18_r75367(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out MethodReference calledMethod, out OpCode callOpcode, Func<ProxyCreatorInfo, uint, uint> getRid) {
			var sig = module.GetSignatureBlob(info.field);
			int len = sig.Length;
			uint magic = (uint)((sig[len - 2] << 24) | (sig[len - 3] << 16) | (sig[len - 5] << 8) | sig[len - 6]);
			uint rid = getRid(creatorInfo, magic);
			int token = (sig[len - 7] << 24) | (int)rid;
			uint table = (uint)token >> 24;
			if (table != 6 && table != 0x0A && table != 0x2B)
				throw new ApplicationException("Invalid method token");
			calledMethod = module.LookupToken(token) as MethodReference;
			callOpcode = getCallOpCode(creatorInfo, info.field);
		}

		static OpCode getCallOpCode(ProxyCreatorInfo info, FieldDefinition field) {
			switch (info.proxyCreatorType) {
			case ProxyCreatorType.CallOrCallvirt:
				if (field.Name.Length > 0 && field.Name[0] == info.callvirtChar)
					return OpCodes.Callvirt;
				return OpCodes.Call;

			case ProxyCreatorType.Newobj:
				return OpCodes.Newobj;

			default: throw new NotSupportedException();
			}
		}

		// A method token is not a stable value so this method can fail to return the correct method!
		// There's nothing I can do about that. It's an obfuscator bug. It was fixed in 1.3 r55346.
		MethodReference createMethodReference(AssemblyNameReference asmRef, uint methodToken) {
			var asm = AssemblyResolver.Instance.Resolve(asmRef);
			if (asm == null)
				return null;

			var method = asm.MainModule.LookupToken((int)methodToken) as MethodDefinition;
			if (method == null)
				return null;

			return module.Import(method);
		}

		static AssemblyNameReference readAssemblyNameReference(BinaryReader reader) {
			var name = readString(reader);
			var version = new Version(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
			var culture = readString(reader);
			byte[] pkt = reader.ReadBoolean() ? reader.ReadBytes(8) : null;
			return new AssemblyNameReference(name, version) {
				Culture = culture,
				PublicKeyToken = pkt,
			};
		}

		static string readString(BinaryReader reader) {
			int len = reader.ReadByte();
			var bytes = new byte[len];
			for (int i = 0; i < len; i++)
				bytes[i] = (byte)(reader.ReadByte() ^ len);
			return Encoding.UTF8.GetString(bytes);
		}

		static OpCode getCallOpCode(ProxyCreatorInfo info, bool isCallvirt) {
			switch (info.proxyCreatorType) {
			case ProxyCreatorType.Newobj:
				return OpCodes.Newobj;

			case ProxyCreatorType.CallOrCallvirt:
				return isCallvirt ? OpCodes.Callvirt : OpCodes.Call;

			default: throw new NotImplementedException();
			}
		}

		public void findDelegateCreator(ISimpleDeobfuscator simpleDeobfuscator) {
			var type = DotNetUtils.getModuleType(module);
			if (type == null)
				return;
			foreach (var method in type.Methods) {
				if (method.Body == null || !method.IsStatic || !method.IsAssembly)
					continue;
				ConfuserVersion theVersion = ConfuserVersion.Unknown;

				if (DotNetUtils.isMethod(method, "System.Void", "(System.String,System.RuntimeFieldHandle)"))
					theVersion = ConfuserVersion.v10_r42915;
				else if (DotNetUtils.isMethod(method, "System.Void", "(System.RuntimeFieldHandle)"))
					theVersion = ConfuserVersion.v10_r48717;
				else
					continue;

				var proxyType = getProxyCreatorType(method);
				if (proxyType == ProxyCreatorType.None)
					continue;

				simpleDeobfuscator.deobfuscate(method);
				MethodDefinition nativeMethod = null;
				uint magic;
				if (findMagic_v14_r58564(method, out magic)) {
					if (!DotNetUtils.callsMethod(method, "System.Byte[] System.Convert::FromBase64String(System.String)"))
						theVersion = ConfuserVersion.v14_r58564;
					else
						theVersion = ConfuserVersion.v14_r58857;
				}
				else if (!DotNetUtils.callsMethod(method, "System.Byte[] System.Convert::FromBase64String(System.String)") &&
					DotNetUtils.callsMethod(method, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)")) {
					if (proxyType == ProxyCreatorType.CallOrCallvirt && !findCallvirtChar(method, out callvirtChar))
						continue;
					if ((nativeMethod = findNativeMethod_v18_r75367(method)) != null)
						theVersion = ConfuserVersion.v18_r75367_native;
					else if (findMagic_v18_r75367(method, out magic))
						theVersion = ConfuserVersion.v18_r75367_normal;
					else if (findMagic_v19_r76101(method, out magic))
						theVersion = ConfuserVersion.v19_r76101_normal;
					else if ((nativeMethod = findNativeMethod_v19_r76101(method)) != null)
						theVersion = ConfuserVersion.v19_r76101_native;
					else {
						if (proxyType == ProxyCreatorType.CallOrCallvirt && !DotNetUtils.callsMethod(method, "System.Int32 System.String::get_Length()"))
							theVersion = ConfuserVersion.v11_r50378;
						int numCalls = countCalls(method, "System.Byte[] System.Text.Encoding::GetBytes(System.Char[],System.Int32,System.Int32)");
						if (numCalls == 2)
							theVersion = ConfuserVersion.v12_r54564;
					}
				}
				else if (is_v17_r73740(method)) {
					if (DotNetUtils.callsMethod(method, "System.Boolean System.Type::get_IsArray()")) {
						if ((nativeMethod = findNativeMethod_v17_r73740(method)) != null)
							theVersion = ConfuserVersion.v17_r74708_native;
						else if (findMagic_v17_r73740(method, out magic))
							theVersion = ConfuserVersion.v17_r74708_normal;
						else
							continue;
					}
					else {
						if ((nativeMethod = findNativeMethod_v17_r73740(method)) != null)
							theVersion = ConfuserVersion.v17_r73740_native;
						else if (findMagic_v17_r73740(method, out magic))
							theVersion = ConfuserVersion.v17_r73740_normal;
						else
							continue;
					}
				}
				else if (theVersion == ConfuserVersion.v10_r42915) {
					if (DeobUtils.hasInteger(method, 0x06000000))
						theVersion = ConfuserVersion.v10_r42919;
				}

				setDelegateCreatorMethod(method);
				methodToInfo.add(method, new ProxyCreatorInfo(method, proxyType, theVersion, magic, nativeMethod, callvirtChar));
				version = (ConfuserVersion)Math.Max((int)version, (int)theVersion);
			}
		}

		static int countCalls(MethodDefinition method, string methodFullName) {
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt && instr.OpCode.Code != Code.Newobj)
					continue;
				var calledMethod = instr.Operand as MethodReference;
				if (calledMethod == null)
					continue;
				if (calledMethod.FullName != methodFullName)
					continue;

				count++;
			}
			return count;
		}

		static bool findMagic_v19_r76101(MethodDefinition method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 7; i++) {
				var ldci4_1 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4_1) || DotNetUtils.getLdcI4Value(ldci4_1) != 24)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Shl)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Or)
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 3]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 4]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 5]))
					continue;
				var ldci4_2 = instrs[i + 6];
				if (!DotNetUtils.isLdcI4(ldci4_2))
					continue;
				if (instrs[i + 7].OpCode.Code != Code.Xor)
					continue;

				magic = (uint)DotNetUtils.getLdcI4Value(ldci4_2);
				return true;
			}
			magic = 0;
			return false;
		}

		static MethodDefinition findNativeMethod_v19_r76101(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 6; i++) {
				var ldci4 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4) || DotNetUtils.getLdcI4Value(ldci4) != 24)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Shl)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Or)
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 3]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 4]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 5]))
					continue;
				var call = instrs[i + 6];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null || calledMethod.Body != null || !calledMethod.IsNative)
					continue;

				return calledMethod;
			}
			return null;
		}

		static bool findMagic_v18_r75367(MethodDefinition method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Reflection.Module System.Reflection.MemberInfo::get_Module()");
				if (i < 0 || i + 3 >= instrs.Count)
					break;

				if (!DotNetUtils.isLdloc(instrs[i + 1]))
					continue;
				var ldci4 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i+3].OpCode.Code != Code.Xor)
					continue;

				magic = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			magic = 0;
			return false;
		}

		static MethodDefinition findNativeMethod_v18_r75367(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Reflection.Module System.Reflection.MemberInfo::get_Module()");
				if (i < 0 || i + 2 >= instrs.Count)
					break;

				if (!DotNetUtils.isLdloc(instrs[i + 1]))
					continue;

				var call = instrs[i + 2];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null || calledMethod.Body != null || !calledMethod.IsNative)
					continue;

				return calledMethod;
			}
			return null;
		}

		static bool findMagic_v17_r73740(MethodDefinition method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Call, "System.Int32 System.BitConverter::ToInt32(System.Byte[],System.Int32)");
				if (index < 0)
					break;
				if (index < 1 || index + 2 >= instrs.Count)
					continue;

				if (!DotNetUtils.isLdcI4(instrs[index - 1]))
					continue;
				var ldci4 = instrs[index + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[index + 2].OpCode.Code != Code.Xor)
					continue;

				magic = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			magic = 0;
			return false;
		}

		static MethodDefinition findNativeMethod_v17_r73740(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Call, "System.Int32 System.BitConverter::ToInt32(System.Byte[],System.Int32)");
				if (index < 0)
					break;
				if (index < 1 || index + 1 >= instrs.Count)
					continue;

				if (!DotNetUtils.isLdcI4(instrs[index - 1]))
					continue;
				var call = instrs[index + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDefinition;
				if (calledMethod == null || calledMethod.Body != null || !calledMethod.IsNative)
					continue;

				return calledMethod;
			}
			return null;
		}

		static bool is_v17_r73740(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)");
				if (index < 0)
					break;
				if (index < 3)
					continue;

				index -= 3;
				var ldci4 = instrs[index];
				if (!DotNetUtils.isLdcI4(ldci4) || DotNetUtils.getLdcI4Value(ldci4) != 24)
					continue;
				if (instrs[index + 1].OpCode.Code != Code.Shl)
					continue;
				if (instrs[index + 2].OpCode.Code != Code.Or)
					continue;

				return true;
			}
			return false;
		}

		static bool findMagic_v14_r58564(MethodDefinition method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Call, "System.Int32 System.BitConverter::ToInt32(System.Byte[],System.Int32)");
				if (index < 0)
					break;
				int index2 = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)");
				if (index2 < 0 || index2 - index != 3)
					continue;
				var ldci4 = instrs[index + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[index + 2].OpCode.Code != Code.Xor)
					continue;

				magic = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			magic = 0;
			return false;
		}

		static ProxyCreatorType getProxyCreatorType(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				var field = instr.Operand as FieldReference;
				if (field == null)
					continue;
				switch (field.FullName) {
				case "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Call":
				case "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Callvirt":
					return ProxyCreatorType.CallOrCallvirt;

				case "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Newobj":
					return ProxyCreatorType.Newobj;
				}
			}
			return ProxyCreatorType.None;
		}

		public new void find() {
			if (delegateCreatorMethods.Count == 0)
				return;
			var cctor = DotNetUtils.getModuleTypeCctor(module);
			if (cctor == null)
				return;

			Log.v("Finding all proxy delegates");

			var delegateInfos = createDelegateInitInfos(cctor);
			fieldToMethods = createFieldToMethodsDictionary(cctor.DeclaringType);
			if (delegateInfos.Count < fieldToMethods.Count)
				throw new ApplicationException("Missing proxy delegates");
			var delegateToFields = new Dictionary<TypeDefinition, List<FieldDefinition>>();
			foreach (var field in fieldToMethods.getKeys()) {
				List<FieldDefinition> list;
				if (!delegateToFields.TryGetValue((TypeDefinition)field.FieldType, out list))
					delegateToFields[(TypeDefinition)field.FieldType] = list = new List<FieldDefinition>();
				list.Add(field);
			}

			foreach (var kv in delegateToFields) {
				var type = kv.Key;
				var fields = kv.Value;

				Log.v("Found proxy delegate: {0} ({1:X8})", Utils.removeNewlines(type), type.MetadataToken.ToInt32());
				RemovedDelegateCreatorCalls++;

				Log.indent();
				foreach (var field in fields) {
					var proxyMethods = fieldToMethods.find(field);
					if (proxyMethods == null)
						continue;
					var info = delegateInfos.find(field);
					if (info == null)
						throw new ApplicationException("Missing proxy info");

					MethodReference calledMethod;
					OpCode callOpcode;
					getCallInfo(info, field, out calledMethod, out callOpcode);

					if (calledMethod == null)
						continue;
					foreach (var proxyMethod in proxyMethods) {
						add(proxyMethod, new DelegateInfo(field, calledMethod, callOpcode));
						Log.v("Field: {0}, Opcode: {1}, Method: {2} ({3:X8})",
									Utils.removeNewlines(field.Name),
									callOpcode,
									Utils.removeNewlines(calledMethod),
									calledMethod.MetadataToken.ToUInt32());
					}
				}
				Log.deIndent();
				delegateTypesDict[type] = true;
			}

			// 1.2 r54564 (almost 1.3) now moves method proxy init code to the delegate cctors
			find2();
		}

		FieldDefinitionAndDeclaringTypeDict<DelegateInitInfo> createDelegateInitInfos(MethodDefinition method) {
			switch (version) {
			case ConfuserVersion.v10_r42915:
			case ConfuserVersion.v10_r42919:
				return createDelegateInitInfos_v10_r42915(method);
			default:
				return createDelegateInitInfos_v10_r48717(method);
			}
		}

		FieldDefinitionAndDeclaringTypeDict<DelegateInitInfo> createDelegateInitInfos_v10_r42915(MethodDefinition method) {
			var infos = new FieldDefinitionAndDeclaringTypeDict<DelegateInitInfo>();
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldstr = instrs[i];
				if (ldstr.OpCode.Code != Code.Ldstr)
					continue;
				var info = ldstr.Operand as string;
				if (info == null)
					continue;

				var ldtoken = instrs[i + 1];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var delegateField = ldtoken.Operand as FieldDefinition;
				if (delegateField == null)
					continue;
				var delegateType = delegateField.FieldType as TypeDefinition;
				if (!DotNetUtils.derivesFromDelegate(delegateType))
					continue;

				var call = instrs[i + 2];
				if (call.OpCode.Code != Code.Call)
					continue;
				var delegateCreatorMethod = call.Operand as MethodDefinition;
				if (delegateCreatorMethod == null || !isDelegateCreatorMethod(delegateCreatorMethod))
					continue;

				infos.add(delegateField, new DelegateInitInfo(info, delegateField, delegateCreatorMethod));
				i += 2;
			}
			return infos;
		}

		FieldDefinitionAndDeclaringTypeDict<DelegateInitInfo> createDelegateInitInfos_v10_r48717(MethodDefinition method) {
			var infos = new FieldDefinitionAndDeclaringTypeDict<DelegateInitInfo>();
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldtoken = instrs[i];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var delegateField = ldtoken.Operand as FieldDefinition;
				if (delegateField == null)
					continue;
				var delegateType = delegateField.FieldType as TypeDefinition;
				if (!DotNetUtils.derivesFromDelegate(delegateType))
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var delegateCreatorMethod = call.Operand as MethodDefinition;
				if (delegateCreatorMethod == null || !isDelegateCreatorMethod(delegateCreatorMethod))
					continue;

				infos.add(delegateField, new DelegateInitInfo(delegateField, delegateCreatorMethod));
				i += 1;
			}
			return infos;
		}

		static FieldDefinitionAndDeclaringTypeDict<List<MethodDefinition>> createFieldToMethodsDictionary(TypeDefinition type) {
			var dict = new FieldDefinitionAndDeclaringTypeDict<List<MethodDefinition>>();
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null || method.Name == ".cctor")
					continue;
				var delegateField = getDelegateField(method);
				if (delegateField == null)
					continue;
				var methods = dict.find(delegateField);
				if (methods == null)
					dict.add(delegateField, methods = new List<MethodDefinition>());
				methods.Add(method);
			}
			return dict;
		}

		static FieldDefinition getDelegateField(MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;

			FieldDefinition field = null;
			bool foundInvoke = false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Ldsfld) {
					var field2 = instr.Operand as FieldDefinition;
					if (field2 == null || field2.DeclaringType != method.DeclaringType)
						continue;
					if (field != null)
						return null;
					if (!DotNetUtils.derivesFromDelegate(field2.FieldType as TypeDefinition))
						continue;
					field = field2;
				}
				else if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
					var calledMethod = instr.Operand as MethodReference;
					foundInvoke |= calledMethod != null && calledMethod.Name == "Invoke";
				}
			}
			return foundInvoke ? field : null;
		}

		static bool findCallvirtChar(MethodDefinition method, out ushort callvirtChar) {
			var instrs = method.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = ConfuserUtils.findCallMethod(instrs, index, Code.Callvirt, "System.Char System.String::get_Chars(System.Int32)");
				if (index < 0)
					break;

				index++;
				if (index >= instrs.Count)
					break;

				var ldci4 = instrs[index];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				callvirtChar = (ushort)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			callvirtChar = 0;
			return false;
		}

		public void cleanUp() {
			if (!Detected)
				return;
			var cctor = DotNetUtils.getModuleTypeCctor(module);
			if (cctor == null)
				return;
			cctor.Body.Instructions.Clear();
			cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
		}

		public bool getRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v10_r42915:
				minRev = 42915;
				maxRev = 42917;
				return true;

			case ConfuserVersion.v10_r42919:
				minRev = 42919;
				maxRev = 48509;
				return true;

			case ConfuserVersion.v10_r48717:
				minRev = 48717;
				maxRev = 50359;
				return true;

			case ConfuserVersion.v11_r50378:
				minRev = 50378;
				maxRev = 54431;
				return true;

			case ConfuserVersion.v12_r54564:
				minRev = 54564;
				maxRev = 58446;
				return true;

			case ConfuserVersion.v14_r58564:
				minRev = 58564;
				maxRev = 58852;
				return true;

			case ConfuserVersion.v14_r58857:
				minRev = 58857;
				maxRev = 73605;
				return true;

			case ConfuserVersion.v17_r73740_normal:
			case ConfuserVersion.v17_r73740_native:
				minRev = 73740;
				maxRev = 74637;
				return true;

			case ConfuserVersion.v17_r74708_normal:
			case ConfuserVersion.v17_r74708_native:
				minRev = 74708;
				maxRev = 75349;
				return true;

			case ConfuserVersion.v18_r75367_normal:
			case ConfuserVersion.v18_r75367_native:
				minRev = 75367;
				maxRev = 75926;
				return true;

			case ConfuserVersion.v19_r76101_normal:
			case ConfuserVersion.v19_r76101_native:
				minRev = 76101;
				maxRev = int.MaxValue;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
