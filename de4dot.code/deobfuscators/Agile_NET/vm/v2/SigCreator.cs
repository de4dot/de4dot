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
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class MethodSigInfo {
		public HandlerTypeCode TypeCode { get; set; }
		public List<BlockSigInfo> BlockSigInfos { get; private set; }

		public MethodSigInfo(List<BlockSigInfo> blockSigInfos) {
			this.BlockSigInfos = blockSigInfos;
		}

		public MethodSigInfo(List<BlockSigInfo> blockSigInfos, HandlerTypeCode typeCode) {
			this.BlockSigInfos = blockSigInfos;
			this.TypeCode = typeCode;
		}

		public override string ToString() {
			return OpCodeHandlerInfo.GetHandlerName(TypeCode);
		}
	}

	class BlockSigInfo {
		readonly List<int> targets;

		public List<BlockElementHash> Hashes { get; private set; }
		public List<int> Targets {
			get { return targets; }
		}
		public bool HasFallThrough { get; set; }
		public bool EndsInRet { get; set; }

		public BlockSigInfo() {
			this.targets = new List<int>();
			this.Hashes = new List<BlockElementHash>();
		}

		public BlockSigInfo(List<BlockElementHash> hashes, List<int> targets) {
			this.Hashes = hashes;
			this.targets = targets;
		}
	}

	enum BlockElementHash : int {
	}

	class SigCreator {
		const int BASE_INDEX = 0x40000000;
		Blocks blocks;
		Dictionary<object, int> objToId = new Dictionary<object, int>();
		CRC32 hasher = new CRC32();

		public SigCreator() {
		}

		public void AddId(object key, int id) {
			if (key != null)
				objToId[key] = id;
		}

		int? GetId(object key) {
			if (key == null)
				return null;

			int id;
			if (objToId.TryGetValue(key, out id))
				return id;
			return null;
		}

		public List<BlockSigInfo> Create(MethodDef method) {
			blocks = new Blocks(method);
			var allBlocks = blocks.MethodBlocks.GetAllBlocks();

			var blockInfos = new List<BlockSigInfo>(allBlocks.Count);
			foreach (var block in allBlocks) {
				var blockInfo = new BlockSigInfo();
				blockInfo.HasFallThrough = block.FallThrough != null;
				blockInfo.EndsInRet = block.LastInstr.OpCode.Code == Code.Ret;
				blockInfos.Add(blockInfo);
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var info = CalculateHash(instrs, ref i);
					if (info != null)
						blockInfo.Hashes.Add(info.Value);
				}
			}

			for (int i = 0; i < blockInfos.Count; i++) {
				var block = allBlocks[i];
				var blockInfo = blockInfos[i];

				if (block.FallThrough != null)
					blockInfo.Targets.Add(allBlocks.IndexOf(block.FallThrough));
				if (block.Targets != null) {
					foreach (var target in block.Targets)
						blockInfo.Targets.Add(allBlocks.IndexOf(target));
				}
			}

			return blockInfos;
		}

		BlockElementHash? CalculateHash(IList<Instr> instrs, ref int index) {
			hasher.Initialize();
			var instr = instrs[index];
			switch (instr.OpCode.Code) {
			case Code.Beq:
			case Code.Beq_S:
				return GetHash(BASE_INDEX + 0);

			case Code.Bge:
			case Code.Bge_S:
				return GetHash(BASE_INDEX + 1);

			case Code.Bge_Un:
			case Code.Bge_Un_S:
				return GetHash(BASE_INDEX + 2);

			case Code.Bgt:
			case Code.Bgt_S:
				return GetHash(BASE_INDEX + 3);

			case Code.Bgt_Un:
			case Code.Bgt_Un_S:
				return GetHash(BASE_INDEX + 4);

			case Code.Ble:
			case Code.Ble_S:
				return GetHash(BASE_INDEX + 5);

			case Code.Ble_Un:
			case Code.Ble_Un_S:
				return GetHash(BASE_INDEX + 6);

			case Code.Blt:
			case Code.Blt_S:
				return GetHash(BASE_INDEX + 7);

			case Code.Blt_Un:
			case Code.Blt_Un_S:
				return GetHash(BASE_INDEX + 8);

			case Code.Bne_Un:
			case Code.Bne_Un_S:
				return GetHash(BASE_INDEX + 9);

			case Code.Brfalse:
			case Code.Brfalse_S:
				return GetHash(BASE_INDEX + 10);

			case Code.Brtrue:
			case Code.Brtrue_S:
				return GetHash(BASE_INDEX + 11);

			case Code.Switch:
				return GetHash(BASE_INDEX + 12);

			case Code.Ceq:
				return GetHash(BASE_INDEX + 13);

			case Code.Cgt:
				return GetHash(BASE_INDEX + 14);

			case Code.Cgt_Un:
				return GetHash(BASE_INDEX + 15);

			case Code.Clt:
				return GetHash(BASE_INDEX + 16);

			case Code.Clt_Un:
				return GetHash(BASE_INDEX + 17);

			case Code.Ldc_I4:
			case Code.Ldc_I4_0:
			case Code.Ldc_I4_1:
			case Code.Ldc_I4_2:
			case Code.Ldc_I4_3:
			case Code.Ldc_I4_4:
			case Code.Ldc_I4_5:
			case Code.Ldc_I4_6:
			case Code.Ldc_I4_7:
			case Code.Ldc_I4_8:
			case Code.Ldc_I4_M1:
			case Code.Ldc_I4_S:
				return GetHash(instr.GetLdcI4Value());

			case Code.Ldstr:
				return GetHash(instr.Operand as string);

			case Code.Rethrow:
				return GetHash(BASE_INDEX + 18);

			case Code.Throw:
				return GetHash(BASE_INDEX + 19);

			case Code.Call:
			case Code.Callvirt:
				Hash(instr.Operand);
				return (BlockElementHash)hasher.GetHash();

			case Code.Ldfld:
				var field = instr.Operand as FieldDef;
				if (!IsTypeField(field))
					return null;
				if (index + 1 >= instrs.Count || !instrs[index + 1].IsLdcI4())
					return null;
				index++;
				return GetHash(GetFieldId(field));

			default:
				break;
			}
			return null;
		}

		bool IsTypeField(FieldDef fd) {
			return fd != null && fd.DeclaringType == blocks.Method.DeclaringType;
		}

		static int GetFieldId(FieldDef fd) {
			if (fd == null)
				return int.MinValue;
			var fieldType = fd.FieldSig.GetFieldType();
			if (fieldType == null)
				return int.MinValue + 1;

			int result = BASE_INDEX + 0x1000;
			for (int i = 0; i < 100; i++) {
				result += (int)fieldType.ElementType;
				if (fieldType.Next == null)
					break;
				result += 0x100;
				fieldType = fieldType.Next;
			}

			var td = fieldType.TryGetTypeDef();
			if (td != null && td.IsEnum)
				return result + 0x10000000;
			return result;
		}

		void Hash(object op) {
			var md = op as MethodDef;
			if (md != null) {
				Hash(md);
				return;
			}

			var mr = op as MemberRef;
			if (mr != null) {
				Hash(mr);
				return;
			}

			var td = op as TypeDef;
			if (td != null) {
				Hash(td);
				return;
			}

			var tr = op as TypeRef;
			if (tr != null) {
				Hash(tr);
				return;
			}

			var ts = op as TypeSpec;
			if (ts != null) {
				Hash(ts);
				return;
			}

			var fsig = op as FieldSig;
			if (fsig != null) {
				Hash(fsig);
				return;
			}

			var msig = op as MethodSig;
			if (msig != null) {
				Hash(msig);
				return;
			}

			var gsig = op as GenericInstMethodSig;
			if (gsig != null) {
				Hash(gsig);
				return;
			}

			var asmRef = op as AssemblyRef;
			if (asmRef != null) {
				Hash(asmRef);
				return;
			}

			var tsig = op as TypeSig;
			if (tsig != null) {
				Hash(tsig);
				return;
			}

			return;
		}

		void Hash(TypeSig sig) {
			Hash(sig, 0);
		}

		void Hash(TypeSig sig, int level) {
			if (sig == null)
				return;
			if (level++ > 20)
				return;

			hasher.Hash((byte)0x41);
			var etype = sig.GetElementType();
			hasher.Hash((byte)etype);
			switch (etype) {
			case ElementType.Ptr:
			case ElementType.ByRef:
			case ElementType.SZArray:
			case ElementType.Pinned:
				Hash(sig.Next, level);
				break;

			case ElementType.Array:
				var arySig = (ArraySig)sig;
				hasher.Hash(arySig.Rank);
				hasher.Hash(arySig.Sizes.Count);
				hasher.Hash(arySig.LowerBounds.Count);
				Hash(sig.Next, level);
				break;

			case ElementType.CModReqd:
			case ElementType.CModOpt:
				Hash(((ModifierSig)sig).Modifier);
				Hash(sig.Next, level);
				break;

			case ElementType.ValueArray:
				hasher.Hash(((ValueArraySig)sig).Size);
				Hash(sig.Next, level);
				break;

			case ElementType.Module:
				hasher.Hash(((ModuleSig)sig).Index);
				Hash(sig.Next, level);
				break;

			case ElementType.GenericInst:
				var gis = (GenericInstSig)sig;
				Hash(gis.GenericType, level);
				foreach (var ga in gis.GenericArguments)
					Hash(ga, level);
				Hash(sig.Next, level);
				break;

			case ElementType.FnPtr:
				Hash(((FnPtrSig)sig).Signature);
				break;

			case ElementType.Var:
			case ElementType.MVar:
				hasher.Hash(((GenericSig)sig).Number);
				break;

			case ElementType.ValueType:
			case ElementType.Class:
				Hash(((TypeDefOrRefSig)sig).TypeDefOrRef);
				break;

			case ElementType.End:
			case ElementType.Void:
			case ElementType.Boolean:
			case ElementType.Char:
			case ElementType.I1:
			case ElementType.U1:
			case ElementType.I2:
			case ElementType.U2:
			case ElementType.I4:
			case ElementType.U4:
			case ElementType.I8:
			case ElementType.U8:
			case ElementType.R4:
			case ElementType.R8:
			case ElementType.String:
			case ElementType.TypedByRef:
			case ElementType.I:
			case ElementType.U:
			case ElementType.R:
			case ElementType.Object:
			case ElementType.Internal:
			case ElementType.Sentinel:
			default:
				break;
			}
		}

		void Hash(MethodDef md) {
			if (md == null)
				return;

			var attrMask1 = MethodImplAttributes.CodeTypeMask | MethodImplAttributes.ManagedMask |
							MethodImplAttributes.ForwardRef | MethodImplAttributes.PreserveSig |
							MethodImplAttributes.InternalCall;
			hasher.Hash((ushort)(md == null ? 0 : md.ImplAttributes & attrMask1));
			var attrMask2 = MethodAttributes.Static | MethodAttributes.Virtual |
							MethodAttributes.HideBySig | MethodAttributes.VtableLayoutMask |
							MethodAttributes.CheckAccessOnOverride | MethodAttributes.Abstract |
							MethodAttributes.SpecialName | MethodAttributes.PinvokeImpl |
							MethodAttributes.UnmanagedExport | MethodAttributes.RTSpecialName;
			hasher.Hash((ushort)(md.Attributes & attrMask2));
			Hash(md.Signature);
			hasher.Hash(md.ParamDefs.Count);
			hasher.Hash(md.GenericParameters.Count);
			hasher.Hash(md.HasImplMap ? 1 : 0);

			var id = GetId(md);
			if (id != null)
				hasher.Hash(id.Value);
		}

		void Hash(MemberRef mr) {
			if (mr == null)
				return;

			Hash(mr.Class);
			if (IsFromNonObfuscatedAssembly(mr.Class))
				Hash(mr.Name);
			Hash(mr.Signature);
		}

		void Hash(TypeDef td) {
			if (td == null)
				return;

			Hash(td.BaseType);
			var attrMask = TypeAttributes.LayoutMask | TypeAttributes.ClassSemanticsMask |
							TypeAttributes.Abstract | TypeAttributes.SpecialName |
							TypeAttributes.Import | TypeAttributes.WindowsRuntime |
							TypeAttributes.StringFormatMask | TypeAttributes.RTSpecialName;
			hasher.Hash((uint)(td.Attributes & attrMask));
			hasher.Hash(td.GenericParameters.Count);
			hasher.Hash(td.Interfaces.Count);
			foreach (var iface in td.Interfaces)
				Hash(iface.Interface);
			var id = GetId(td);
			if (id != null)
				hasher.Hash(id.Value);
		}

		void Hash(TypeRef tr) {
			if (tr == null)
				return;

			Hash(tr.ResolutionScope);
			if (IsFromNonObfuscatedAssembly(tr)) {
				Hash(tr.Namespace);
				Hash(tr.Name);
			}
		}

		void Hash(TypeSpec ts) {
			if (ts == null)
				return;

			Hash(ts.TypeSig);
		}

		void Hash(FieldSig sig) {
			if (sig == null)
				return;

			hasher.Hash((byte)sig.GetCallingConvention());
			Hash(sig.GetFieldType());
		}

		void Hash(MethodSig sig) {
			if (sig == null)
				return;

			hasher.Hash((byte)sig.GetCallingConvention());
			Hash(sig.GetRetType());
			foreach (var p in sig.GetParams())
				Hash(p);
			hasher.Hash(sig.GetParamCount());
			if (sig.GetParamsAfterSentinel() != null) {
				foreach (var p in sig.GetParamsAfterSentinel())
					Hash(p);
			}
		}

		void Hash(GenericInstMethodSig sig) {
			if (sig == null)
				return;

			hasher.Hash((byte)sig.GetCallingConvention());
			foreach (var ga in sig.GetGenericArguments())
				Hash(ga);
		}

		void Hash(AssemblyRef asmRef) {
			if (asmRef == null)
				return;

			bool canWriteAsm = IsNonObfuscatedAssembly(asmRef);
			hasher.Hash(canWriteAsm ? 1 : 0);
			if (canWriteAsm) {
				bool hasPk = !PublicKeyBase.IsNullOrEmpty2(asmRef.PublicKeyOrToken);
				if (hasPk)
					hasher.Hash(PublicKeyBase.ToPublicKeyToken(asmRef.PublicKeyOrToken).Data);
				Hash(asmRef.Name);
				Hash(asmRef.Culture);
			}
		}

		void Hash(string s) {
			if (s != null)
				hasher.Hash(Encoding.UTF8.GetBytes(s));
		}

		BlockElementHash GetHash(int val) {
			hasher.Hash(val);
			return (BlockElementHash)hasher.GetHash();
		}

		BlockElementHash GetHash(string s) {
			Hash(s);
			return (BlockElementHash)hasher.GetHash();
		}

		static bool IsFromNonObfuscatedAssembly(IMemberRefParent mrp) {
			return IsFromNonObfuscatedAssembly(mrp as TypeRef);
		}

		static bool IsFromNonObfuscatedAssembly(TypeRef tr) {
			if (tr == null)
				return false;

			for (int i = 0; i < 100; i++) {
				var asmRef = tr.ResolutionScope as AssemblyRef;
				if (asmRef != null)
					return IsNonObfuscatedAssembly(asmRef);

				var tr2 = tr.ResolutionScope as TypeRef;
				if (tr2 != null) {
					tr = tr2;
					continue;
				}

				break;
			}

			return false;
		}

		static bool IsNonObfuscatedAssembly(IAssembly asm) {
			if (asm == null)
				return false;

			// The only external asm refs it uses...
			if (asm.Name != "mscorlib" && asm.Name != "System")
				return false;

			return true;
		}
	}
}
