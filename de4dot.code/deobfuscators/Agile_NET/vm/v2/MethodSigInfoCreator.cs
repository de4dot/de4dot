/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.MD;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class MethodSigInfo {
		readonly List<BlockInfo> blockInfos;

		public List<BlockInfo> BlockInfos {
			get { return blockInfos; }
		}

		public MethodSigInfo() {
			this.blockInfos = new List<BlockInfo>();
		}

		public MethodSigInfo(IEnumerable<BlockInfo> blockInfos) {
			this.blockInfos = new List<BlockInfo>(blockInfos);
		}
	}

	class BlockInfo : IEquatable<BlockInfo> {
		readonly List<int> targets;

		public byte[] Hash { get; set; }
		public List<int> Targets {
			get { return targets; }
		}

		public BlockInfo() {
			this.targets = new List<int>();
		}

		public BlockInfo(byte[] hash, IEnumerable<int> targets) {
			this.Hash = hash;
			this.targets = new List<int>(targets);
		}

		public override string ToString() {
			if (Hash == null)
				return "<null>";
			return BitConverter.ToString(Hash).Replace("-", string.Empty);
		}

		public bool Equals(BlockInfo other) {
			return Equals(Hash, other.Hash) &&
				Targets.Count == other.Targets.Count;
		}

		bool Equals(byte[] a, byte[] b) {
			if (a == b)
				return true;
			if (a == null || b == null)
				return false;
			if (a.Length != b.Length)
				return false;
			for (int i = 0; i < a.Length; i++) {
				if (a[i] != b[i])
					return false;
			}
			return true;
		}
	}

	class MethodSigInfoCreator {
		MethodSigInfo methodSigInfo;
		Blocks blocks;
		IList<Block> allBlocks;
		Dictionary<Block, BlockInfo> blockToInfo;
		Dictionary<object, int> methodToId = new Dictionary<object, int>();

		public void AddId(object key, int id) {
			if (key != null)
				methodToId[key] = id;
		}

		int GetId(object key) {
			if (key == null)
				return int.MinValue;

			int id;
			if (methodToId.TryGetValue(key, out id))
				return id;
			return int.MinValue + 1;
		}

		public MethodSigInfo Create(Blocks blocks) {
			methodSigInfo = new MethodSigInfo();

			this.blocks = blocks;
			allBlocks = blocks.MethodBlocks.GetAllBlocks();

			blockToInfo = new Dictionary<Block, BlockInfo>();
			foreach (var block in allBlocks) {
				var blockInfo = new BlockInfo();
				blockToInfo[block] = blockInfo;
				methodSigInfo.BlockInfos.Add(blockInfo);
			}

			foreach (var block in allBlocks) {
				var blockInfo = blockToInfo[block];
				Update(blockInfo, block);
				if (block.FallThrough != null)
					blockInfo.Targets.Add(allBlocks.IndexOf(block.FallThrough));
				if (block.Targets != null) {
					foreach (var target in block.Targets)
						blockInfo.Targets.Add(allBlocks.IndexOf(target));
				}
			}

			return methodSigInfo;
		}

		void Update(BlockInfo blockInfo, Block block) {
			using (var hasher = MD5.Create()) {
				bool emptyHash;
				using (var outStream = new NullStream()) {
					using (var csStream = new CryptoStream(outStream, hasher, CryptoStreamMode.Write)) {
						var writer = new BinaryWriter(csStream);
						Update(writer, blockInfo, block);
					}
					emptyHash = outStream.Length == 0;
				}
				if (!emptyHash)
					blockInfo.Hash = hasher.Hash;
			}
		}

		void Update(BinaryWriter writer, BlockInfo blockInfo, Block block) {
			var instrs = block.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				switch (instr.OpCode.Code) {
				case Code.Beq_S:
				case Code.Bge_S:
				case Code.Bgt_S:
				case Code.Ble_S:
				case Code.Blt_S:
				case Code.Bne_Un_S:
				case Code.Bge_Un_S:
				case Code.Bgt_Un_S:
				case Code.Ble_Un_S:
				case Code.Blt_Un_S:
				case Code.Brfalse_S:
				case Code.Brtrue_S:
				case Code.Leave_S:
				case Code.Beq:
				case Code.Bge:
				case Code.Bgt:
				case Code.Ble:
				case Code.Blt:
				case Code.Bne_Un:
				case Code.Bge_Un:
				case Code.Bgt_Un:
				case Code.Ble_Un:
				case Code.Blt_Un:
				case Code.Brfalse:
				case Code.Brtrue:
				case Code.Leave:
					writer.Write((ushort)SimplifyBranch(instr.OpCode.Code));
					break;

				case Code.Switch:
					writer.Write((ushort)instr.OpCode.Code);
					writer.Write(blockInfo.Targets.Count);
					break;

				case Code.Br_S:
				case Code.Br:
					break;

				case Code.Ret:
					break;

				case Code.Ldc_I4_M1:
				case Code.Ldc_I4_0:
				case Code.Ldc_I4_1:
				case Code.Ldc_I4_2:
				case Code.Ldc_I4_3:
				case Code.Ldc_I4_4:
				case Code.Ldc_I4_5:
				case Code.Ldc_I4_6:
				case Code.Ldc_I4_7:
				case Code.Ldc_I4_8:
				case Code.Ldc_I4:
				case Code.Ldc_I4_S:
					writer.Write((ushort)Code.Ldc_I4);
					writer.Write(instr.GetLdcI4Value());
					break;

				case Code.Ldc_I8:
					writer.Write((ushort)instr.OpCode.Code);
					writer.Write((long)instr.Operand);
					break;

				case Code.Ldc_R4:
					writer.Write((ushort)instr.OpCode.Code);
					writer.Write((float)instr.Operand);
					break;

				case Code.Ldc_R8:
					writer.Write((ushort)instr.OpCode.Code);
					writer.Write((double)instr.Operand);
					break;

				case Code.Ldfld:
					var typeField = instr.Operand as FieldDef;
					bool isField = IsTypeField(typeField);
					writer.Write((ushort)instr.OpCode.Code);
					writer.Write(isField);
					if (isField) {
						if (i + 1 < instrs.Count && instrs[i + 1].IsLdcI4())
							i++;
						writer.Write(GetFieldId(typeField));
					}
					else
						Write(writer, instr.Operand);
					break;

				case Code.Call:
				case Code.Callvirt:
				case Code.Newobj:
				case Code.Jmp:
				case Code.Ldftn:
				case Code.Ldvirtftn:
				case Code.Ldtoken:
				case Code.Stfld:
				case Code.Ldsfld:
				case Code.Stsfld:
				case Code.Ldflda:
				case Code.Ldsflda:
				case Code.Cpobj:
				case Code.Ldobj:
				case Code.Castclass:
				case Code.Isinst:
				case Code.Unbox:
				case Code.Stobj:
				case Code.Box:
				case Code.Newarr:
				case Code.Ldelema:
				case Code.Ldelem:
				case Code.Stelem:
				case Code.Unbox_Any:
				case Code.Refanyval:
				case Code.Mkrefany:
				case Code.Initobj:
				case Code.Constrained:
				case Code.Sizeof:
					writer.Write((ushort)instr.OpCode.Code);
					Write(writer, instr.Operand);
					break;

				case Code.Ldstr:
					writer.Write((ushort)instr.OpCode.Code);
					break;

				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					writer.Write((ushort)Code.Ldarg);
					writer.Write(instr.Instruction.GetParameterIndex());
					break;

				case Code.Ldarga:
				case Code.Ldarga_S:
					writer.Write((ushort)Code.Ldarga);
					writer.Write(instr.Instruction.GetParameterIndex());
					break;

				case Code.Starg:
				case Code.Starg_S:
					writer.Write((ushort)Code.Starg);
					writer.Write(instr.Instruction.GetParameterIndex());
					break;

				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
					writer.Write((ushort)Code.Ldloc);
					break;

				case Code.Ldloca:
				case Code.Ldloca_S:
					writer.Write((ushort)Code.Ldloca);
					break;

				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
					writer.Write((ushort)Code.Stloc);
					break;

				case Code.Ldnull:
				case Code.Throw:
				case Code.Rethrow:
				case Code.Ldlen:
				case Code.Ckfinite:
				case Code.Arglist:
				case Code.Localloc:
				case Code.Volatile:
				case Code.Tailcall:
				case Code.Cpblk:
				case Code.Initblk:
				case Code.Refanytype:
				case Code.Readonly:
				case Code.Break:
				case Code.Endfinally:
				case Code.Endfilter:
					writer.Write((ushort)instr.OpCode.Code);
					break;

				case Code.Calli:
					writer.Write((ushort)instr.OpCode.Code);
					Write(writer, instr.Operand);
					break;

				case Code.Unaligned:
					writer.Write((ushort)instr.OpCode.Code);
					writer.Write((byte)instr.Operand);
					break;

				default:
					break;
				}
			}
		}

		void Write(BinaryWriter writer, object op) {
			var fd = op as FieldDef;
			if (fd != null) {
				Write(writer, fd);
				return;
			}

			var mr = op as MemberRef;
			if (mr != null) {
				Write(writer, mr);
				return;
			}

			var md = op as MethodDef;
			if (md != null) {
				Write(writer, md);
				return;
			}

			var ms = op as MethodSpec;
			if (ms != null) {
				Write(writer, ms);
				return;
			}

			var td = op as TypeDef;
			if (td != null) {
				Write(writer, td);
				return;
			}

			var tr = op as TypeRef;
			if (tr != null) {
				Write(writer, tr);
				return;
			}

			var ts = op as TypeSpec;
			if (ts != null) {
				Write(writer, ts);
				return;
			}

			var fsig = op as FieldSig;
			if (fsig != null) {
				Write(writer, fsig);
				return;
			}

			var msig = op as MethodSig;
			if (msig != null) {
				Write(writer, msig);
				return;
			}

			var gsig = op as GenericInstMethodSig;
			if (gsig != null) {
				Write(writer, gsig);
				return;
			}

			var asmRef = op as AssemblyRef;
			if (asmRef != null) {
				Write(writer, asmRef);
				return;
			}

			writer.Write((byte)ObjectType.Unknown);
		}

		enum ObjectType : byte {
			// 00..3F = Table.XXX values.
			Unknown = 0x40,
			TypeSig = 0x41,
			FieldSig = 0x42,
			MethodSig = 0x43,
			GenericInstMethodSig = 0x44,
		}

		void Write(BinaryWriter writer, TypeSig sig) {
			Write(writer, sig, 0);
		}

		void Write(BinaryWriter writer, TypeSig sig, int level) {
			if (level++ > 20)
				return;

			writer.Write((byte)ObjectType.TypeSig);
			var etype = sig.GetElementType();
			writer.Write((byte)etype);
			switch (etype) {
			case ElementType.Ptr:
			case ElementType.ByRef:
			case ElementType.SZArray:
			case ElementType.Pinned:
				Write(writer, sig.Next, level);
				break;

			case ElementType.Array:
				var arySig = (ArraySig)sig;
				writer.Write(arySig.Rank);
				writer.Write(arySig.Sizes.Count);
				writer.Write(arySig.LowerBounds.Count);
				Write(writer, sig.Next, level);
				break;

			case ElementType.CModReqd:
			case ElementType.CModOpt:
				Write(writer, ((ModifierSig)sig).Modifier);
				Write(writer, sig.Next, level);
				break;

			case ElementType.ValueArray:
				writer.Write(((ValueArraySig)sig).Size);
				Write(writer, sig.Next, level);
				break;

			case ElementType.Module:
				writer.Write(((ModuleSig)sig).Index);
				Write(writer, sig.Next, level);
				break;

			case ElementType.GenericInst:
				var gis = (GenericInstSig)sig;
				Write(writer, gis.GenericType, level);
				foreach (var ga in gis.GenericArguments)
					Write(writer, ga, level);
				Write(writer, sig.Next, level);
				break;

			case ElementType.FnPtr:
				Write(writer, ((FnPtrSig)sig).Signature);
				break;

			case ElementType.Var:
			case ElementType.MVar:
				writer.Write(((GenericSig)sig).Number);
				break;

			case ElementType.ValueType:
			case ElementType.Class:
				Write(writer, ((TypeDefOrRefSig)sig).TypeDefOrRef);
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

		void Write(BinaryWriter writer, FieldSig sig) {
			writer.Write((byte)ObjectType.FieldSig);
			writer.Write((byte)(sig == null ? 0 : sig.GetCallingConvention()));
			Write(writer, sig.GetFieldType());
		}

		void Write(BinaryWriter writer, MethodSig sig) {
			writer.Write((byte)ObjectType.MethodSig);
			writer.Write((byte)(sig == null ? 0 : sig.GetCallingConvention()));
			Write(writer, sig.GetRetType());
			foreach (var p in sig.GetParams())
				Write(writer, p);
			writer.Write(sig.GetParamCount());
			bool hasParamsAfterSentinel = sig.GetParamsAfterSentinel() != null;
			writer.Write(hasParamsAfterSentinel);
			if (hasParamsAfterSentinel) {
				foreach (var p in sig.GetParamsAfterSentinel())
					Write(writer, p);
			}
		}

		void Write(BinaryWriter writer, GenericInstMethodSig sig) {
			writer.Write((byte)ObjectType.GenericInstMethodSig);
			writer.Write((byte)(sig == null ? 0 : sig.GetCallingConvention()));
			foreach (var ga in sig.GetGenericArguments())
				Write(writer, ga);
		}

		void Write(BinaryWriter writer, FieldDef fd) {
			writer.Write((byte)Table.Field);
			Write(writer, fd.DeclaringType);
			var attrMask = FieldAttributes.Static | FieldAttributes.InitOnly |
							FieldAttributes.Literal | FieldAttributes.SpecialName |
							FieldAttributes.PinvokeImpl | FieldAttributes.RTSpecialName;
			writer.Write((ushort)(fd == null ? 0 : fd.Attributes & attrMask));
			Write(writer, fd == null ? null : fd.Signature);
		}

		void Write(BinaryWriter writer, MemberRef mr) {
			writer.Write((byte)Table.MemberRef);
			var parent = mr == null ? null : mr.Class;
			Write(writer, parent);
			bool canWriteName = IsFromNonObfuscatedAssembly(parent);
			writer.Write(canWriteName);
			if (canWriteName)
				writer.Write(mr.Name);
			Write(writer, mr == null ? null : mr.Signature);
		}

		void Write(BinaryWriter writer, MethodDef md) {
			writer.Write((byte)Table.Method);
			Write(writer, md.DeclaringType);
			var attrMask1 = MethodImplAttributes.CodeTypeMask | MethodImplAttributes.ManagedMask |
							MethodImplAttributes.ForwardRef | MethodImplAttributes.PreserveSig |
							MethodImplAttributes.InternalCall;
			writer.Write((ushort)(md == null ? 0 : md.ImplAttributes & attrMask1));
			var attrMask2 = MethodAttributes.Static | MethodAttributes.Virtual |
							MethodAttributes.HideBySig | MethodAttributes.VtableLayoutMask |
							MethodAttributes.CheckAccessOnOverride | MethodAttributes.Abstract |
							MethodAttributes.SpecialName | MethodAttributes.PinvokeImpl |
							MethodAttributes.UnmanagedExport | MethodAttributes.RTSpecialName;
			writer.Write((ushort)(md == null ? 0 : md.Attributes & attrMask2));
			Write(writer, md == null ? null : md.Signature);
			writer.Write(md == null ? 0 : md.ParamDefs.Count);
			writer.Write(md == null ? 0 : md.GenericParameters.Count);
			writer.Write(md == null ? false : md.HasImplMap);
			writer.Write(GetId(md));
		}

		void Write(BinaryWriter writer, MethodSpec ms) {
			writer.Write((byte)Table.MethodSpec);
			Write(writer, ms == null ? null : ms.Method);
			Write(writer, ms == null ? null : ms.Instantiation);
		}

		void Write(BinaryWriter writer, TypeDef td) {
			writer.Write((byte)Table.TypeDef);
			Write(writer, td == null ? null : td.BaseType);
			var attrMask = TypeAttributes.LayoutMask | TypeAttributes.ClassSemanticsMask |
							TypeAttributes.Abstract | TypeAttributes.SpecialName |
							TypeAttributes.Import | TypeAttributes.WindowsRuntime |
							TypeAttributes.StringFormatMask | TypeAttributes.RTSpecialName;
			writer.Write((uint)(td == null ? 0 : td.Attributes & attrMask));
			Write(writer, td == null ? null : td.BaseType);
			writer.Write(td == null ? 0 : td.GenericParameters.Count);
			writer.Write(td == null ? 0 : td.Interfaces.Count);
			if (td != null) {
				foreach (var iface in td.Interfaces)
					Write(writer, iface);
			}
			writer.Write(GetId(td));
		}

		void Write(BinaryWriter writer, TypeRef tr) {
			writer.Write((byte)Table.TypeRef);
			Write(writer, tr == null ? null : tr.ResolutionScope);
			bool canWriteName = IsFromNonObfuscatedAssembly(tr);
			writer.Write(canWriteName);
			if (canWriteName) {
				writer.Write(tr.Namespace);
				writer.Write(tr.Name);
			}
		}

		void Write(BinaryWriter writer, TypeSpec ts) {
			writer.Write((byte)Table.TypeSpec);
			Write(writer, ts == null ? null : ts.TypeSig);
		}

		void Write(BinaryWriter writer, AssemblyRef asmRef) {
			writer.Write((byte)Table.AssemblyRef);

			bool canWriteAsm = IsNonObfuscatedAssembly(asmRef);
			writer.Write(canWriteAsm);
			if (canWriteAsm) {
				bool hasPk = !PublicKeyBase.IsNullOrEmpty2(asmRef.PublicKeyOrToken);
				writer.Write(hasPk);
				if (hasPk)
					writer.Write(PublicKeyBase.ToPublicKeyToken(asmRef.PublicKeyOrToken).Data);
				writer.Write(asmRef.Name);
				writer.Write(asmRef.Culture);
			}
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

		static bool IsNonObfuscatedAssembly(AssemblyRef asmRef) {
			if (asmRef == null)
				return false;
			// The only external asm refs it uses...
			if (asmRef.Name != "mscorlib" && asmRef.Name != "System")
				return false;

			return true;
		}

		bool IsTypeField(FieldDef fd) {
			return fd != null && fd.DeclaringType == blocks.Method.DeclaringType;
		}

		int GetFieldId(FieldDef fd) {
			if (fd == null)
				return int.MinValue;
			var fieldType = fd.FieldSig.GetFieldType();
			if (fieldType == null)
				return int.MinValue + 1;

			int result = 0;
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

		static Code SimplifyBranch(Code code) {
			switch (code) {
			case Code.Beq_S:		return Code.Beq;
			case Code.Bge_S:		return Code.Bge;
			case Code.Bgt_S:		return Code.Bgt;
			case Code.Ble_S:		return Code.Ble;
			case Code.Blt_S:		return Code.Blt;
			case Code.Bne_Un_S:		return Code.Bne_Un;
			case Code.Bge_Un_S:		return Code.Bge_Un;
			case Code.Bgt_Un_S:		return Code.Bgt_Un;
			case Code.Ble_Un_S:		return Code.Ble_Un;
			case Code.Blt_Un_S:		return Code.Blt_Un;
			case Code.Br_S:			return Code.Br;
			case Code.Brfalse_S:	return Code.Brfalse;
			case Code.Brtrue_S:		return Code.Brtrue;
			case Code.Leave_S:		return Code.Leave;
			default:				return code;
			}
		}
	}
}
