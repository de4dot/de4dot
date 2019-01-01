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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class ProxyCallFixer : ProxyCallFixer2, IVersionProvider, IDisposable {
		MethodDefAndDeclaringTypeDict<ProxyCreatorInfo> methodToInfo = new MethodDefAndDeclaringTypeDict<ProxyCreatorInfo>();
		FieldDefAndDeclaringTypeDict<List<MethodDef>> fieldToMethods = new FieldDefAndDeclaringTypeDict<List<MethodDef>>();
		string ourAsm;
		ConfuserVersion version = ConfuserVersion.Unknown;
		byte[] fileData;
		X86Emulator x86emu;
		ushort callvirtChar;
		bool foundNewobjProxy;

		enum ConfuserVersion {
			Unknown,
			v10_r42915,
			v10_r42919,
			v10_r48717,
			v11_r50378,
			v12_r54564,
			v13_r55346,
			v13_r55604,
			v14_r58564,
			v14_r58802,
			v14_r58857,
			v16_r66631,
			v16_r70489,
			v17_r73479,
			v17_r73740_normal,
			v17_r73740_native,
			v17_r74708_normal,
			v17_r74708_native,
			v18_r75367_normal,
			v18_r75367_native,
			v18_r75369_normal,
			v18_r75369_native,
			v19_r76101_normal,
			v19_r76101_native,
			v19_r78363_normal,
			v19_r78363_native,
			v19_r78963_normal_Newobj,
			v19_r78963_native_Newobj,
		}

		enum ProxyCreatorType {
			None,
			CallOrCallvirt,
			Newobj,
		}

		class ProxyCreatorInfo {
			public readonly MethodDef creatorMethod;
			public readonly ProxyCreatorType proxyCreatorType;
			public readonly ConfuserVersion version;
			public readonly uint magic;
			public readonly MethodDef nativeMethod;
			public readonly ushort callvirtChar;

			public ProxyCreatorInfo(MethodDef creatorMethod, ProxyCreatorType proxyCreatorType, ConfuserVersion version, uint magic, MethodDef nativeMethod, ushort callvirtChar) {
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
			public readonly FieldDef field;
			public readonly MethodDef creatorMethod;

			public DelegateInitInfo(FieldDef field, MethodDef creatorMethod) {
				this.field = field;
				this.creatorMethod = creatorMethod;
			}

			public DelegateInitInfo(string data, FieldDef field, MethodDef creatorMethod) {
				this.data = Convert.FromBase64String(data);
				this.field = field;
				this.creatorMethod = creatorMethod;
			}
		}

		protected override bool ProxyCallIsObfuscated => true;

		public IEnumerable<FieldDef> Fields {
			get {
				var fields = new List<FieldDef>(fieldToMethods.GetKeys());
				var type = DotNetUtils.GetModuleType(module);
				if (fields.Count > 0 && type != null) {
					foreach (var field in type.Fields) {
						var fieldType = field.FieldType.TryGetTypeDef();
						if (fieldType != null && delegateTypesDict.ContainsKey(fieldType))
							fields.Add(field);
					}
				}
				return fields;
			}
		}

		public override IEnumerable<Tuple<MethodDef, string>> OtherMethods {
			get {
				var list = new List<Tuple<MethodDef, string>>();
				foreach (var info in methodToInfo.GetValues()) {
					list.Add(new Tuple<MethodDef, string> {
						Item1 = info.creatorMethod,
						Item2 = "Delegate creator method",
					});
					list.Add(new Tuple<MethodDef, string> {
						Item1 = info.nativeMethod,
						Item2 = "Calculate RID native method",
					});
				}
				foreach (var methods in fieldToMethods.GetValues()) {
					foreach (var method in methods) {
						list.Add(new Tuple<MethodDef, string> {
							Item1 = method,
							Item2 = "Proxy delegate method",
						});
					}
				}
				return list;
			}
		}

		public ProxyCallFixer(ModuleDefMD module, byte[] fileData)
			: base(module) {
			this.fileData = fileData;
			if (module.Assembly == null)
				ourAsm = " -1-1-1-1-1- , Version=1.2.3.4, Culture=neutral, PublicKeyToken=null";
			else
				ourAsm = module.Assembly.FullName;
		}

		protected override object CheckCctor(TypeDef type, MethodDef cctor) {
			// Here if 1.2 r54564 (almost 1.3) or later

			var fieldToInfo = new FieldDefAndDeclaringTypeDict<DelegateInitInfo>();

			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldtoken = instrs[i];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDef;
				if (field == null || field.DeclaringType != cctor.DeclaringType)
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null)
					continue;
				if (!IsDelegateCreatorMethod(calledMethod))
					continue;
				var info = methodToInfo.Find(calledMethod);
				if (info == null)
					continue;

				i++;
				fieldToInfo.Add(field, new DelegateInitInfo(field, calledMethod));
			}
			return fieldToInfo.Count == 0 ? null : fieldToInfo;
		}

		protected override void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode) {
			var info = context as DelegateInitInfo;
			if (info == null) {
				if (context is FieldDefAndDeclaringTypeDict<DelegateInitInfo> fieldToInfo)
					info = fieldToInfo.Find(field);
			}
			if (info == null)
				throw new ApplicationException("Couldn't get the delegate info");
			var creatorInfo = methodToInfo.Find(info.creatorMethod);

			switch (creatorInfo.version) {
			case ConfuserVersion.v10_r42915:
			case ConfuserVersion.v10_r42919:
				GetCallInfo_v10_r42915(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v10_r48717:
			case ConfuserVersion.v11_r50378:
			case ConfuserVersion.v12_r54564:
			case ConfuserVersion.v13_r55346:
			case ConfuserVersion.v13_r55604:
			case ConfuserVersion.v14_r58564:
			case ConfuserVersion.v14_r58802:
				GetCallInfo_v10_r48717(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v14_r58857:
			case ConfuserVersion.v16_r66631:
			case ConfuserVersion.v16_r70489:
			case ConfuserVersion.v17_r73479:
				GetCallInfo_v14_r58857(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v17_r73740_normal:
			case ConfuserVersion.v17_r74708_normal:
				GetCallInfo_v17_r73740_normal(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v17_r73740_native:
			case ConfuserVersion.v17_r74708_native:
				GetCallInfo_v17_r73740_native(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v18_r75367_normal:
			case ConfuserVersion.v18_r75369_normal:
			case ConfuserVersion.v19_r76101_normal:
			case ConfuserVersion.v19_r78363_normal:
			case ConfuserVersion.v19_r78963_normal_Newobj:
				GetCallInfo_v18_r75367_normal(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			case ConfuserVersion.v18_r75367_native:
			case ConfuserVersion.v18_r75369_native:
			case ConfuserVersion.v19_r76101_native:
			case ConfuserVersion.v19_r78363_native:
			case ConfuserVersion.v19_r78963_native_Newobj:
				GetCallInfo_v18_r75367_native(info, creatorInfo, out calledMethod, out callOpcode);
				break;

			default:
				throw new ApplicationException("Unknown version");
			}

			if (calledMethod == null) {
				Logger.w("Could not find real method. Proxy field: {0:X8}", info.field.MDToken.ToInt32());
				errors++;
			}
		}

		void GetCallInfo_v10_r42915(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out IMethod calledMethod, out OpCode callOpcode) {
			var reader = new BinaryReader(new MemoryStream(info.data));

			bool isCallvirt = false;
			if (creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt)
				isCallvirt = reader.ReadBoolean();

			var asmRef = ReadAssemblyNameReference(reader);
			// If < 1.0 r42919, then high byte is 06, else it's cleared.
			uint token = (reader.ReadUInt32() & 0x00FFFFFF) | 0x06000000;
			if (reader.BaseStream.Position != reader.BaseStream.Length)
				throw new ApplicationException("Extra data");

			if (asmRef.FullName == ourAsm)
				calledMethod = module.ResolveToken(token) as IMethod;
			else
				calledMethod = CreateMethodReference(asmRef, token);

			callOpcode = GetCallOpCode(creatorInfo, isCallvirt);
		}

		void GetCallInfo_v10_r48717(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out IMethod calledMethod, out OpCode callOpcode) {
			bool isNew = creatorInfo.version == ConfuserVersion.v14_r58802;

			int offs = creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt ? 2 : 1;
			if (isNew)
				offs--;
			int callvirtOffs = isNew ? 0 : 1;

			// This is an obfuscator bug. Field names are stored in the #Strings heap,
			// and strings in that heap are UTF8 zero terminated strings, but Confuser
			// can generate names with zeros in them. This was fixed in 1.4 58857.
			if (offs + 2 > info.field.Name.String.Length) {
				calledMethod = null;
				callOpcode = OpCodes.Call;
				return;
			}

			uint token = BitConverter.ToUInt32(Encoding.Unicode.GetBytes(info.field.Name.String.ToCharArray(), offs, 2), 0) ^ creatorInfo.magic;
			uint table = token >> 24;
			if (table != 0 && table != 6 && table != 0x0A && table != 0x2B)
				throw new ApplicationException("Invalid method token");

			// 1.3 r55346 now correctly uses method reference tokens and finally fixed the old
			// bug of using methoddef tokens to reference external methods.
			if (isNew || info.field.Name.String[0] == (char)1 || table != 0x06)
				calledMethod = module.ResolveToken(token) as IMethod;
			else {
				var asmRef = module.ResolveAssemblyRef((uint)info.field.Name.String[0] - 2 + 1);
				calledMethod = CreateMethodReference(asmRef, token);
			}

			bool isCallvirt = false;
			if (creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt && info.field.Name.String[callvirtOffs] == '\r')
				isCallvirt = true;
			callOpcode = GetCallOpCode(creatorInfo, isCallvirt);
		}

		void GetCallInfo_v14_r58857(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out IMethod calledMethod, out OpCode callOpcode) {
			int offs = creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt ? 1 : 0;
			var nameInfo = DecryptFieldName(info.field.Name.String);

			uint token = BitConverter.ToUInt32(nameInfo, offs) ^ creatorInfo.magic;
			uint table = token >> 24;
			if (table != 6 && table != 0x0A && table != 0x2B)
				throw new ApplicationException("Invalid method token");

			calledMethod = module.ResolveToken(token) as IMethod;

			bool isCallvirt = false;
			if (creatorInfo.proxyCreatorType == ProxyCreatorType.CallOrCallvirt && nameInfo[0] == '\r')
				isCallvirt = true;
			callOpcode = GetCallOpCode(creatorInfo, isCallvirt);
		}

		static byte[] DecryptFieldName(string name) {
			var chars = new char[name.Length];
			for (int i = 0; i < chars.Length; i++)
				chars[i] = (char)((byte)name[i] ^ i);
			return Convert.FromBase64CharArray(chars, 0, chars.Length);
		}

		void Extract_v17_r73740(ProxyCreatorInfo creatorInfo, byte[] nameInfo, out uint arg, out uint table, out bool isCallvirt) {
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

		void GetCallInfo_v17_r73740_normal(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out IMethod calledMethod, out OpCode callOpcode) {
			var nameInfo = DecryptFieldName(info.field.Name.String);
			Extract_v17_r73740(creatorInfo, nameInfo, out uint arg, out uint table, out bool isCallvirt);
			uint token = (arg ^ creatorInfo.magic) | table;

			calledMethod = module.ResolveToken((int)token) as IMethod;
			callOpcode = GetCallOpCode(creatorInfo, isCallvirt);
		}

		void GetCallInfo_v17_r73740_native(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out IMethod calledMethod, out OpCode callOpcode) {
			var nameInfo = DecryptFieldName(info.field.Name.String);
			Extract_v17_r73740(creatorInfo, nameInfo, out uint arg, out uint table, out bool isCallvirt);
			if (x86emu == null)
				x86emu = new X86Emulator(fileData);
			uint token = x86emu.Emulate((uint)creatorInfo.nativeMethod.RVA, arg) | table;

			calledMethod = module.ResolveToken((int)token) as IMethod;
			callOpcode = GetCallOpCode(creatorInfo, isCallvirt);
		}

		void GetCallInfo_v18_r75367_normal(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out IMethod calledMethod, out OpCode callOpcode) =>
			GetCallInfo_v18_r75367(info, creatorInfo, out calledMethod, out callOpcode, (creatorInfo2, magic) => creatorInfo2.magic ^ magic);

		void GetCallInfo_v18_r75367_native(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out IMethod calledMethod, out OpCode callOpcode) =>
			GetCallInfo_v18_r75367(info, creatorInfo, out calledMethod, out callOpcode, (creatorInfo2, magic) => {
				if (x86emu == null)
					x86emu = new X86Emulator(fileData);
				return x86emu.Emulate((uint)creatorInfo2.nativeMethod.RVA, magic);
			});

		void GetCallInfo_v18_r75367(DelegateInitInfo info, ProxyCreatorInfo creatorInfo, out IMethod calledMethod, out OpCode callOpcode, Func<ProxyCreatorInfo, uint, uint> getRid) {
			var sig = module.ReadBlob(info.field.MDToken.Raw);
			int len = sig.Length;
			uint magic = (uint)((sig[len - 2] << 24) | (sig[len - 3] << 16) | (sig[len - 5] << 8) | sig[len - 6]);
			uint rid = getRid(creatorInfo, magic);
			int token = (sig[len - 7] << 24) | (int)rid;
			uint table = (uint)token >> 24;
			if (table != 6 && table != 0x0A && table != 0x2B)
				throw new ApplicationException("Invalid method token");
			calledMethod = module.ResolveToken(token) as IMethod;
			callOpcode = GetCallOpCode(creatorInfo, info.field);
		}

		static OpCode GetCallOpCode(ProxyCreatorInfo info, FieldDef field) {
			switch (info.proxyCreatorType) {
			case ProxyCreatorType.CallOrCallvirt:
				if (field.Name.String.Length > 0 && field.Name.String[0] == info.callvirtChar)
					return OpCodes.Callvirt;
				return OpCodes.Call;

			case ProxyCreatorType.Newobj:
				return OpCodes.Newobj;

			default: throw new NotSupportedException();
			}
		}

		// A method token is not a stable value so this method can fail to return the correct method!
		// There's nothing I can do about that. It's an obfuscator bug. It was fixed in 1.3 r55346.
		IMethod CreateMethodReference(AssemblyRef asmRef, uint methodToken) {
			var asm = module.Context.AssemblyResolver.Resolve(asmRef, module);
			if (asm == null)
				return null;

			var method = ((ModuleDefMD)asm.ManifestModule).ResolveToken(methodToken) as MethodDef;
			if (method == null)
				return null;

			return module.Import(method);
		}

		AssemblyRef ReadAssemblyNameReference(BinaryReader reader) {
			var name = ReadString(reader);
			var version = new Version(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
			var culture = ReadString(reader);
			byte[] pkt = reader.ReadBoolean() ? reader.ReadBytes(8) : null;
			return module.UpdateRowId(new AssemblyRefUser(name, version, pkt == null ? null : new PublicKeyToken(pkt), culture));
		}

		static string ReadString(BinaryReader reader) {
			int len = reader.ReadByte();
			var bytes = new byte[len];
			for (int i = 0; i < len; i++)
				bytes[i] = (byte)(reader.ReadByte() ^ len);
			return Encoding.UTF8.GetString(bytes);
		}

		static OpCode GetCallOpCode(ProxyCreatorInfo info, bool isCallvirt) {
			switch (info.proxyCreatorType) {
			case ProxyCreatorType.Newobj:
				return OpCodes.Newobj;

			case ProxyCreatorType.CallOrCallvirt:
				return isCallvirt ? OpCodes.Callvirt : OpCodes.Call;

			default: throw new NotImplementedException();
			}
		}

		public void FindDelegateCreator(ISimpleDeobfuscator simpleDeobfuscator) {
			var type = DotNetUtils.GetModuleType(module);
			if (type == null)
				return;
			foreach (var method in type.Methods) {
				if (method.Body == null || !method.IsStatic || !method.IsAssembly)
					continue;
				var theVersion = ConfuserVersion.Unknown;

				if (DotNetUtils.IsMethod(method, "System.Void", "(System.String,System.RuntimeFieldHandle)"))
					theVersion = ConfuserVersion.v10_r42915;
				else if (DotNetUtils.IsMethod(method, "System.Void", "(System.RuntimeFieldHandle)"))
					theVersion = ConfuserVersion.v10_r48717;
				else
					continue;

				var proxyType = GetProxyCreatorType(method, simpleDeobfuscator, out int tmpVer);
				if (proxyType == ProxyCreatorType.None)
					continue;
				if (proxyType == ProxyCreatorType.Newobj)
					foundNewobjProxy = true;

				simpleDeobfuscator.Deobfuscate(method, SimpleDeobfuscatorFlags.DisableConstantsFolderExtraInstrs);
				MethodDef nativeMethod = null;
				if (FindMagic_v14_r58564(method, out uint magic)) {
					if (!DotNetUtils.CallsMethod(method, "System.Byte[] System.Convert::FromBase64String(System.String)")) {
						if (!IsMethodCreator_v14_r58802(method, proxyType))
							theVersion = ConfuserVersion.v14_r58564;
						else
							theVersion = ConfuserVersion.v14_r58802;
					}
					else if (DotNetUtils.CallsMethod(method, "System.Reflection.Module System.Reflection.MemberInfo::get_Module()"))
						theVersion = ConfuserVersion.v17_r73479;
					else if (proxyType != ProxyCreatorType.CallOrCallvirt || !HasFieldReference(method, "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Castclass"))
						theVersion = ConfuserVersion.v14_r58857;
					else if (proxyType == ProxyCreatorType.CallOrCallvirt && DotNetUtils.CallsMethod(method, "System.Void System.Reflection.Emit.DynamicMethod::.ctor(System.String,System.Type,System.Type[],System.Boolean)"))
						theVersion = ConfuserVersion.v16_r66631;
					else if (proxyType == ProxyCreatorType.CallOrCallvirt)
						theVersion = ConfuserVersion.v16_r70489;
				}
				else if (!DotNetUtils.CallsMethod(method, "System.Byte[] System.Convert::FromBase64String(System.String)") &&
					DotNetUtils.CallsMethod(method, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)")) {
					if (proxyType == ProxyCreatorType.CallOrCallvirt && !FindCallvirtChar(method, out callvirtChar))
						continue;
					if ((nativeMethod = FindNativeMethod_v18_r75367(method)) != null)
						theVersion = proxyType != ProxyCreatorType.CallOrCallvirt || callvirtChar == 9 ? ConfuserVersion.v18_r75367_native : ConfuserVersion.v18_r75369_native;
					else if (FindMagic_v18_r75367(method, out magic))
						theVersion = proxyType != ProxyCreatorType.CallOrCallvirt || callvirtChar == 9 ? ConfuserVersion.v18_r75367_normal : ConfuserVersion.v18_r75369_normal;
					else if (FindMagic_v19_r76101(method, out magic))
						CommonCheckVersion19(method, true, tmpVer, ref theVersion);
					else if ((nativeMethod = FindNativeMethod_v19_r76101(method)) != null)
						CommonCheckVersion19(method, false, tmpVer, ref theVersion);
					else {
						if (proxyType == ProxyCreatorType.CallOrCallvirt && !DotNetUtils.CallsMethod(method, "System.Int32 System.String::get_Length()"))
							theVersion = ConfuserVersion.v11_r50378;
						int numCalls = ConfuserUtils.CountCalls(method, "System.Byte[] System.Text.Encoding::GetBytes(System.Char[],System.Int32,System.Int32)");
						if (numCalls == 2)
							theVersion = ConfuserVersion.v12_r54564;
						if (!DotNetUtils.CallsMethod(method, "System.Reflection.Assembly System.Reflection.Assembly::Load(System.Reflection.AssemblyName)"))
							theVersion = ConfuserVersion.v13_r55346;
						if (DotNetUtils.CallsMethod(method, "System.Void System.Runtime.CompilerServices.RuntimeHelpers::RunClassConstructor(System.RuntimeTypeHandle)"))
							theVersion = ConfuserVersion.v13_r55604;
					}
				}
				else if (Is_v17_r73740(method)) {
					if (DotNetUtils.CallsMethod(method, "System.Boolean System.Type::get_IsArray()")) {
						if ((nativeMethod = FindNativeMethod_v17_r73740(method)) != null)
							theVersion = ConfuserVersion.v17_r74708_native;
						else if (FindMagic_v17_r73740(method, out magic))
							theVersion = ConfuserVersion.v17_r74708_normal;
						else
							continue;
					}
					else {
						if ((nativeMethod = FindNativeMethod_v17_r73740(method)) != null)
							theVersion = ConfuserVersion.v17_r73740_native;
						else if (FindMagic_v17_r73740(method, out magic))
							theVersion = ConfuserVersion.v17_r73740_normal;
						else
							continue;
					}
				}
				else if (theVersion == ConfuserVersion.v10_r42915) {
					if (DeobUtils.HasInteger(method, 0x06000000))
						theVersion = ConfuserVersion.v10_r42919;
				}

				SetDelegateCreatorMethod(method);
				methodToInfo.Add(method, new ProxyCreatorInfo(method, proxyType, theVersion, magic, nativeMethod, callvirtChar));
				version = (ConfuserVersion)Math.Max((int)version, (int)theVersion);
			}
		}

		static bool CommonCheckVersion19(MethodDef method, bool isNormal, int tmpProxyVer, ref ConfuserVersion theVersion) {
			if (tmpProxyVer == 1) {
				theVersion = isNormal ? ConfuserVersion.v19_r76101_normal : ConfuserVersion.v19_r76101_native;
				return true;
			}
			else if (tmpProxyVer == 2) {
				if (!CheckCtorProxyType_v19_r78963(method))
					theVersion = isNormal ? ConfuserVersion.v19_r78363_normal : ConfuserVersion.v19_r78363_native;
				else
					theVersion = isNormal ? ConfuserVersion.v19_r78963_normal_Newobj : ConfuserVersion.v19_r78963_native_Newobj;
				return true;
			}

			return false;
		}

		static bool HasFieldReference(MethodDef method, string fieldFullName) {
			foreach (var instr in method.Body.Instructions) {
				var field = instr.Operand as IField;
				if (field == null)
					continue;
				if (field.FullName == fieldFullName)
					return true;
			}
			return false;
		}

		static bool IsMethodCreator_v14_r58802(MethodDef method, ProxyCreatorType proxyType) {
			int index = GetFieldNameIndex(method);
			if (index < 0)
				throw new ApplicationException("Could not find field name index");
			switch (proxyType) {
			case ProxyCreatorType.Newobj:
				if (index == 1)
					return false;
				if (index == 0)
					return true;
				break;

			case ProxyCreatorType.CallOrCallvirt:
				if (index == 2)
					return false;
				if (index == 1)
					return true;
				break;

			default: throw new ApplicationException("Invalid proxy creator type");
			}

			throw new ApplicationException("Could not find field name index");
		}

		static int GetFieldNameIndex(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Byte[] System.Text.Encoding::GetBytes(System.Char[],System.Int32,System.Int32)");
				if (i < 0)
					break;
				if (i < 2)
					continue;
				var ldci4 = instrs[i - 2];
				if (!ldci4.IsLdcI4())
					continue;

				return ldci4.GetLdcI4Value();
			}
			return -1;
		}

		static bool FindMagic_v19_r76101(MethodDef method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 7; i++) {
				var ldci4_1 = instrs[i];
				if (!ldci4_1.IsLdcI4() || ldci4_1.GetLdcI4Value() != 24)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Shl)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Or)
					continue;
				if (!instrs[i + 3].IsStloc())
					continue;
				if (!instrs[i + 4].IsLdloc())
					continue;
				if (!instrs[i + 5].IsLdloc())
					continue;
				var ldci4_2 = instrs[i + 6];
				if (!ldci4_2.IsLdcI4())
					continue;
				if (instrs[i + 7].OpCode.Code != Code.Xor)
					continue;

				magic = (uint)ldci4_2.GetLdcI4Value();
				return true;
			}
			magic = 0;
			return false;
		}

		static MethodDef FindNativeMethod_v19_r76101(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 6; i++) {
				var ldci4 = instrs[i];
				if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 24)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Shl)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Or)
					continue;
				if (!instrs[i + 3].IsStloc())
					continue;
				if (!instrs[i + 4].IsLdloc())
					continue;
				if (!instrs[i + 5].IsLdloc())
					continue;
				var call = instrs[i + 6];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null || calledMethod.Body != null || !calledMethod.IsNative)
					continue;

				return calledMethod;
			}
			return null;
		}

		static bool FindMagic_v18_r75367(MethodDef method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Reflection.Module System.Reflection.MemberInfo::get_Module()");
				if (i < 0 || i + 3 >= instrs.Count)
					break;

				if (!instrs[i + 1].IsLdloc())
					continue;
				var ldci4 = instrs[i + 2];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i+3].OpCode.Code != Code.Xor)
					continue;

				magic = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			magic = 0;
			return false;
		}

		static MethodDef FindNativeMethod_v18_r75367(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				i = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Reflection.Module System.Reflection.MemberInfo::get_Module()");
				if (i < 0 || i + 2 >= instrs.Count)
					break;

				if (!instrs[i + 1].IsLdloc())
					continue;

				var call = instrs[i + 2];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null || calledMethod.Body != null || !calledMethod.IsNative)
					continue;

				return calledMethod;
			}
			return null;
		}

		static bool FindMagic_v17_r73740(MethodDef method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Call, "System.Int32 System.BitConverter::ToInt32(System.Byte[],System.Int32)");
				if (index < 0)
					break;
				if (index < 1 || index + 2 >= instrs.Count)
					continue;

				if (!instrs[index - 1].IsLdcI4())
					continue;
				var ldci4 = instrs[index + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[index + 2].OpCode.Code != Code.Xor)
					continue;

				magic = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			magic = 0;
			return false;
		}

		static MethodDef FindNativeMethod_v17_r73740(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Call, "System.Int32 System.BitConverter::ToInt32(System.Byte[],System.Int32)");
				if (index < 0)
					break;
				if (index < 1 || index + 1 >= instrs.Count)
					continue;

				if (!instrs[index - 1].IsLdcI4())
					continue;
				var call = instrs[index + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null || calledMethod.Body != null || !calledMethod.IsNative)
					continue;

				return calledMethod;
			}
			return null;
		}

		static bool Is_v17_r73740(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)");
				if (index < 0)
					break;
				if (index < 3)
					continue;

				index -= 3;
				var ldci4 = instrs[index];
				if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 24)
					continue;
				if (instrs[index + 1].OpCode.Code != Code.Shl)
					continue;
				if (instrs[index + 2].OpCode.Code != Code.Or)
					continue;

				return true;
			}
			return false;
		}

		static bool FindMagic_v14_r58564(MethodDef method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.FindCallMethod(instrs, i, Code.Call, "System.Int32 System.BitConverter::ToInt32(System.Byte[],System.Int32)");
				if (index < 0)
					break;
				int index2 = ConfuserUtils.FindCallMethod(instrs, i, Code.Callvirt, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)");
				if (index2 < 0 || index2 - index != 3)
					continue;
				var ldci4 = instrs[index + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[index + 2].OpCode.Code != Code.Xor)
					continue;

				magic = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			magic = 0;
			return false;
		}

		static ProxyCreatorType GetProxyCreatorType(MethodDef method, ISimpleDeobfuscator simpleDeobfuscator, out int version) {
			var type = GetProxyCreatorTypeV1(method);
			if (type != ProxyCreatorType.None) {
				version = 1;
				return type;
			}

			simpleDeobfuscator.Deobfuscate(method);

			type = GetProxyCreatorTypeV2(method);
			if (type != ProxyCreatorType.None) {
				version = 2;
				return type;
			}

			version = 0;
			return ProxyCreatorType.None;
		}

		// <= 1.9 r78342 (refs to System.Reflection.Emit.OpCodes)
		static ProxyCreatorType GetProxyCreatorTypeV1(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				var field = instr.Operand as IField;
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

		// >= 1.9 r78363 (no refs to System.Reflection.Emit.OpCodes)
		static ProxyCreatorType GetProxyCreatorTypeV2(MethodDef method) {
			if (!DeobUtils.HasInteger(method, 0x2A))
				return ProxyCreatorType.None;
			if (CheckCtorProxyTypeV2(method))
				return ProxyCreatorType.Newobj;
			if (CheckCallProxyTypeV2(method))
				return ProxyCreatorType.CallOrCallvirt;
			return ProxyCreatorType.None;
		}

		static bool CheckCtorProxyTypeV2(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4 = instrs[i];
				if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 2)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Mul)
					continue;
				ldci4 = instrs[i + 2];
				if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 0x73)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Stelem_I1)
					continue;

				return true;
			}
			return false;
		}

		static bool CheckCallProxyTypeV2(MethodDef method) =>
			DeobUtils.HasInteger(method, 0x28) &&
			DeobUtils.HasInteger(method, 0x6F);

		// r78963 adds a 'castclass' opcode to the generated code. This code assumes
		// CheckCtorProxyTypeV2() has returned true.
		static bool CheckCtorProxyType_v19_r78963(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				if (instrs[i].OpCode.Code != Code.Add)
					continue;
				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 0x74)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Stelem_I1)
					continue;

				return true;
			}
			return false;
		}

		public new void Find() {
			if (delegateCreatorMethods.Count == 0)
				return;
			var cctor = DotNetUtils.GetModuleTypeCctor(module);
			if (cctor == null)
				return;

			Logger.v("Finding all proxy delegates");

			var delegateInfos = CreateDelegateInitInfos(cctor);
			fieldToMethods = CreateFieldToMethodsDictionary(cctor.DeclaringType);
			if (delegateInfos.Count < fieldToMethods.Count)
				throw new ApplicationException("Missing proxy delegates");
			var delegateToFields = new Dictionary<TypeDef, List<FieldDef>>();
			foreach (var field in fieldToMethods.GetKeys()) {
				if (!delegateToFields.TryGetValue(field.FieldType.TryGetTypeDef(), out var list))
					delegateToFields[field.FieldType.TryGetTypeDef()] = list = new List<FieldDef>();
				list.Add(field);
			}

			foreach (var kv in delegateToFields) {
				var type = kv.Key;
				var fields = kv.Value;

				Logger.v("Found proxy delegate: {0} ({1:X8})", Utils.RemoveNewlines(type), type.MDToken.ToInt32());
				RemovedDelegateCreatorCalls++;

				Logger.Instance.Indent();
				foreach (var field in fields) {
					var proxyMethods = fieldToMethods.Find(field);
					if (proxyMethods == null)
						continue;
					var info = delegateInfos.Find(field);
					if (info == null)
						throw new ApplicationException("Missing proxy info");

					GetCallInfo(info, field, out var calledMethod, out var callOpcode);

					if (calledMethod == null)
						continue;
					foreach (var proxyMethod in proxyMethods) {
						Add(proxyMethod, new DelegateInfo(field, calledMethod, callOpcode));
						Logger.v("Field: {0}, Opcode: {1}, Method: {2} ({3:X8})",
									Utils.RemoveNewlines(field.Name),
									callOpcode,
									Utils.RemoveNewlines(calledMethod),
									calledMethod.MDToken.ToUInt32());
					}
				}
				Logger.Instance.DeIndent();
				delegateTypesDict[type] = true;
			}

			// 1.2 r54564 (almost 1.3) now moves method proxy init code to the delegate cctors
			Find2();
		}

		FieldDefAndDeclaringTypeDict<DelegateInitInfo> CreateDelegateInitInfos(MethodDef method) {
			switch (version) {
			case ConfuserVersion.v10_r42915:
			case ConfuserVersion.v10_r42919:
				return CreateDelegateInitInfos_v10_r42915(method);
			default:
				return CreateDelegateInitInfos_v10_r48717(method);
			}
		}

		FieldDefAndDeclaringTypeDict<DelegateInitInfo> CreateDelegateInitInfos_v10_r42915(MethodDef method) {
			var infos = new FieldDefAndDeclaringTypeDict<DelegateInitInfo>();
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
				var delegateField = ldtoken.Operand as FieldDef;
				if (delegateField == null)
					continue;
				var delegateType = delegateField.FieldType.TryGetTypeDef();
				if (!DotNetUtils.DerivesFromDelegate(delegateType))
					continue;

				var call = instrs[i + 2];
				if (call.OpCode.Code != Code.Call)
					continue;
				var delegateCreatorMethod = call.Operand as MethodDef;
				if (delegateCreatorMethod == null || !IsDelegateCreatorMethod(delegateCreatorMethod))
					continue;

				infos.Add(delegateField, new DelegateInitInfo(info, delegateField, delegateCreatorMethod));
				i += 2;
			}
			return infos;
		}

		FieldDefAndDeclaringTypeDict<DelegateInitInfo> CreateDelegateInitInfos_v10_r48717(MethodDef method) {
			var infos = new FieldDefAndDeclaringTypeDict<DelegateInitInfo>();
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldtoken = instrs[i];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var delegateField = ldtoken.Operand as FieldDef;
				if (delegateField == null)
					continue;
				var delegateType = delegateField.FieldType.TryGetTypeDef();
				if (!DotNetUtils.DerivesFromDelegate(delegateType))
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var delegateCreatorMethod = call.Operand as MethodDef;
				if (delegateCreatorMethod == null || !IsDelegateCreatorMethod(delegateCreatorMethod))
					continue;

				infos.Add(delegateField, new DelegateInitInfo(delegateField, delegateCreatorMethod));
				i += 1;
			}
			return infos;
		}

		static FieldDefAndDeclaringTypeDict<List<MethodDef>> CreateFieldToMethodsDictionary(TypeDef type) {
			var dict = new FieldDefAndDeclaringTypeDict<List<MethodDef>>();
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null || method.Name == ".cctor")
					continue;
				var delegateField = GetDelegateField(method);
				if (delegateField == null)
					continue;
				var methods = dict.Find(delegateField);
				if (methods == null)
					dict.Add(delegateField, methods = new List<MethodDef>());
				methods.Add(method);
			}
			return dict;
		}

		static FieldDef GetDelegateField(MethodDef method) {
			if (method == null || method.Body == null)
				return null;

			FieldDef field = null;
			bool foundInvoke = false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Ldsfld) {
					var field2 = instr.Operand as FieldDef;
					if (field2 == null || field2.DeclaringType != method.DeclaringType)
						continue;
					if (field != null)
						return null;
					if (!DotNetUtils.DerivesFromDelegate(field2.FieldType.TryGetTypeDef()))
						continue;
					field = field2;
				}
				else if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
					var calledMethod = instr.Operand as IMethod;
					foundInvoke |= calledMethod != null && calledMethod.Name == "Invoke";
				}
			}
			return foundInvoke ? field : null;
		}

		static bool FindCallvirtChar(MethodDef method, out ushort callvirtChar) {
			var instrs = method.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = ConfuserUtils.FindCallMethod(instrs, index, Code.Callvirt, "System.Char System.String::get_Chars(System.Int32)");
				if (index < 0)
					break;

				index++;
				if (index >= instrs.Count)
					break;

				var ldci4 = instrs[index];
				if (!ldci4.IsLdcI4())
					continue;
				callvirtChar = (ushort)ldci4.GetLdcI4Value();
				return true;
			}
			callvirtChar = 0;
			return false;
		}

		public void CleanUp() {
			if (!Detected)
				return;
			var cctor = DotNetUtils.GetModuleTypeCctor(module);
			if (cctor == null)
				return;
			cctor.Body.Instructions.Clear();
			cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
		}

		public bool GetRevisionRange(out int minRev, out int maxRev) {
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
				maxRev = 54431;
				return true;

			case ConfuserVersion.v11_r50378:
				minRev = 50378;
				maxRev = 54431;
				return true;

			case ConfuserVersion.v12_r54564:
				minRev = 54564;
				maxRev = 54574;
				return true;

			case ConfuserVersion.v13_r55346:
				minRev = 55346;
				maxRev = 55346;
				return true;

			case ConfuserVersion.v13_r55604:
				minRev = 55604;
				maxRev = 58446;
				return true;

			case ConfuserVersion.v14_r58564:
				minRev = 58564;
				maxRev = 58741;
				return true;

			case ConfuserVersion.v14_r58802:
				minRev = 58802;
				maxRev = 58852;
				return true;

			case ConfuserVersion.v14_r58857:
				minRev = 58857;
				maxRev = 73477;
				return true;

			case ConfuserVersion.v16_r66631:
				minRev = 66631;
				maxRev = 69666;
				return true;

			case ConfuserVersion.v16_r70489:
				minRev = 70489;
				maxRev = 73477;
				return true;

			case ConfuserVersion.v17_r73479:
				minRev = 73479;
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

			case ConfuserVersion.v18_r75369_normal:
			case ConfuserVersion.v18_r75369_native:
				minRev = 75369;
				maxRev = 75926;
				return true;

			case ConfuserVersion.v19_r76101_normal:
			case ConfuserVersion.v19_r76101_native:
				minRev = 76101;
				maxRev = 78342;
				return true;

			case ConfuserVersion.v19_r78363_normal:
			case ConfuserVersion.v19_r78363_native:
				minRev = 78363;
				// We can only detect the r78963 version if a method ctor proxy is used.
				// If it's not used, then maxRev must be the same maxRev as in the next case.
				// If a method ctor proxy is found, then we know that rev <= 78962.
				if (foundNewobjProxy)
					maxRev = 78962;
				else
					maxRev = int.MaxValue;
				return true;

			case ConfuserVersion.v19_r78963_normal_Newobj:
			case ConfuserVersion.v19_r78963_native_Newobj:
				minRev = 78963;
				maxRev = int.MaxValue;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}

		public void Dispose() {
			if (x86emu != null)
				x86emu.Dispose();
			x86emu = null;
		}
	}
}
