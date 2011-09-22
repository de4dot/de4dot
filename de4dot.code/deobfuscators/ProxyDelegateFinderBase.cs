/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators {
	abstract class ProxyDelegateFinderBase {
		ModuleDefinition module;
		IList<MemberReference> memberReferences;
		MethodDefinition delegateCreatorMethod;
		Dictionary<TypeDefinition, bool> delegateTypesDict = new Dictionary<TypeDefinition, bool>();
		Dictionary<FieldReferenceAndDeclaringTypeKey, DelegateInfo> fieldToDelegateInfo = new Dictionary<FieldReferenceAndDeclaringTypeKey, DelegateInfo>();

		class DelegateInfo {
			public MethodReference methodRef;	// Method we should call
			public FieldDefinition field;		// Field holding the Delegate instance
			public bool isVirtual;
			public DelegateInfo(FieldDefinition field, MethodReference methodRef, bool isVirtual) {
				this.field = field;
				this.methodRef = methodRef;
				this.isVirtual = isVirtual;
			}
		}

		public int RemovedDelegateCreatorCalls { get; set; }
		public IEnumerable<TypeDefinition> DelegateTypes {
			get { return delegateTypesDict.Keys; }
		}
		public MethodDefinition DelegateCreatorMethod {
			get { return delegateCreatorMethod; }
		}

		public bool Detected {
			get { return delegateCreatorMethod != null; }
		}

		public ProxyDelegateFinderBase(ModuleDefinition module, IList<MemberReference> memberReferences) {
			this.module = module;
			this.memberReferences = memberReferences;
		}

		public void setDelegateCreatorMethod(MethodDefinition delegateCreatorMethod) {
			this.delegateCreatorMethod = delegateCreatorMethod;
		}

		public void find() {
			if (delegateCreatorMethod == null)
				return;

			Log.v("Finding all proxy delegates");
			foreach (var type in module.Types) {
				if (type.BaseType == null || type.BaseType.FullName != "System.MulticastDelegate")
					continue;
				var cctor = findMethod(type, ".cctor");
				if (cctor == null || !cctor.HasBody)
					continue;
				if (!type.HasFields)
					continue;

				var instrs = cctor.Body.Instructions;
				if (instrs.Count != 3)
					continue;
				if (!DotNetUtils.isLdcI4(instrs[0].OpCode.Code))
					continue;
				if (instrs[1].OpCode != OpCodes.Call || instrs[1].Operand != delegateCreatorMethod)
					continue;
				if (instrs[2].OpCode != OpCodes.Ret)
					continue;

				int delegateToken = 0x02000001 + DotNetUtils.getLdcI4Value(instrs[0]);
				if (type.MetadataToken.ToInt32() != delegateToken) {
					Log.w("Delegate token is not current type");
					continue;
				}

				Log.v("Found proxy delegate: {0} ({1:X8})", type, type.MetadataToken.ToUInt32());
				RemovedDelegateCreatorCalls++;
				Log.indent();
				foreach (var field in type.Fields) {
					if (!field.IsStatic || field.IsPublic)
						continue;

					int methodIndex;
					bool isVirtual;
					getCallInfo(field, out methodIndex, out isVirtual);
					if (methodIndex >= memberReferences.Count)
						throw new ApplicationException(string.Format("methodIndex ({0}) >= memberReferences.Count ({1})", methodIndex, memberReferences.Count));

					var methodRef = memberReferences[methodIndex] as MethodReference;
					if (methodRef == null)
						throw new ApplicationException("methodRef is null");
					fieldToDelegateInfo[new FieldReferenceAndDeclaringTypeKey(field)] = new DelegateInfo(field, methodRef, isVirtual);
					Log.v("Field: {0}, Virtual: {1}, Method: {2}, RID: {3}", field.Name, isVirtual, methodRef, methodIndex + 1);
				}
				Log.deIndent();
				delegateTypesDict[type] = true;
			}
		}

		protected abstract void getCallInfo(FieldDefinition field, out int methodIndex, out bool isVirtual);

		MethodDefinition findMethod(TypeDefinition type, string name) {
			if (!type.HasMethods)
				return null;
			foreach (var method in type.Methods) {
				if (method.Name == name)
					return method;
			}
			return null;
		}

		DelegateInfo getDelegateInfo(FieldReference field) {
			if (field == null)
				return null;
			DelegateInfo di;
			if (fieldToDelegateInfo.TryGetValue(new FieldReferenceAndDeclaringTypeKey(field), out di))
				return di;
			return null;
		}

		class BlockInstr {
			public Block Block { get; set; }
			public int Index { get; set; }
		}

		class RemoveInfo {
			public int Index { get; set; }
			public DelegateInfo DelegateInfo { get; set; }
			public bool IsCall {
				get { return DelegateInfo != null; }
			}
		}

		public void deobfuscate(Blocks blocks) {
			var removeInfos = new Dictionary<Block, List<RemoveInfo>>();

			var allBlocks = blocks.MethodBlocks.getAllBlocks();
			foreach (var block in allBlocks) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var ldsfld = instrs[i];
					if (ldsfld.OpCode != OpCodes.Ldsfld)
						continue;
					var di = getDelegateInfo(ldsfld.Operand as FieldReference);
					if (di == null)
						continue;

					var visited = new Dictionary<Block, bool>();
					var callInfo = findProxyCall(di, block, i, visited, 1);
					if (callInfo != null) {
						add(removeInfos, block, i, null);
						add(removeInfos, callInfo.Block, callInfo.Index, di);
					}
					else {
						Log.w("Could not fix proxy call. Method: {0} ({1:X8}), Proxy type: {2} ({3:X8})",
							blocks.Method, blocks.Method.MetadataToken.ToInt32(),
							di.field.DeclaringType, di.field.DeclaringType.MetadataToken.ToInt32());
					}
				}
			}

			foreach (var block in removeInfos.Keys) {
				var list = removeInfos[block];
				var removeIndexes = new List<int>(list.Count);
				foreach (var info in list) {
					if (info.IsCall) {
						var opcode = info.DelegateInfo.isVirtual ? OpCodes.Callvirt : OpCodes.Call;
						var newInstr = Instruction.Create(opcode, info.DelegateInfo.methodRef);
						block.replace(info.Index, 1, newInstr);
					}
					else
						removeIndexes.Add(info.Index);
				}
				block.remove(removeIndexes);
			}

			fixBrokenCalls(blocks.Method, allBlocks);
		}

		void add(Dictionary<Block, List<RemoveInfo>> removeInfos, Block block, int index, DelegateInfo di) {
			List<RemoveInfo> list;
			if (!removeInfos.TryGetValue(block, out list))
				removeInfos[block] = list = new List<RemoveInfo>();
			list.Add(new RemoveInfo {
				Index = index,
				DelegateInfo = di,
			});
		}

		BlockInstr findProxyCall(DelegateInfo di, Block block, int index, Dictionary<Block, bool> visited, int stack) {
			if (visited.ContainsKey(block))
				return null;
			if (index <= 0)
				visited[block] = true;

			var instrs = block.Instructions;
			for (int i = index + 1; i < instrs.Count; i++) {
				if (stack <= 0)
					return null;
				var instr = instrs[i];
				DotNetUtils.updateStack(instr.Instruction, ref stack);
				if (stack < 0)
					return null;

				if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
					continue;
				var method = DotNetUtils.getMethod(module, instr.Operand as MethodReference);
				if (method == null)
					continue;
				if (stack != (DotNetUtils.hasReturnValue(method) ? 1 : 0))
					continue;
				if (method.DeclaringType != di.field.DeclaringType)
					continue;

				return new BlockInstr {
					Block = block,
					Index = i,
				};
			}
			if (stack <= 0)
				return null;

			foreach (var target in block.getTargets()) {
				var info = findProxyCall(di, target, -1, visited, stack);
				if (info != null)
					return info;
			}

			return null;
		}

		// The obfuscator could be buggy and call a proxy delegate without pushing the
		// instance field. SA has done it, so let's fix it.
		void fixBrokenCalls(MethodDefinition obfuscatedMethod, IList<Block> allBlocks) {
			foreach (var block in allBlocks) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var call = instrs[i];
					if (call.OpCode != OpCodes.Call && call.OpCode != OpCodes.Callvirt)
						continue;
					var methodRef = call.Operand as MethodReference;
					if (methodRef.Name != "Invoke")
						continue;
					var method = DotNetUtils.getMethod(module, methodRef);
					if (method == null)
						continue;
					var declaringType = DotNetUtils.getType(module, method.DeclaringType);
					if (declaringType == null)
						continue;
					if (!delegateTypesDict.ContainsKey(declaringType))
						continue;

					// Oooops!!! The obfuscator is buggy. Well, let's hope it is, or it's my code. ;)

					Log.w("Holy obfuscator bugs, Batman! Found a proxy delegate call with no instance push in {0:X8}. Replacing it with a throw...", obfuscatedMethod.MetadataToken.ToInt32());
					block.insert(i, Instruction.Create(OpCodes.Ldnull));
					block.replace(i + 1, 1, Instruction.Create(OpCodes.Throw));
					i++;
				}
			}
		}
	}
}
