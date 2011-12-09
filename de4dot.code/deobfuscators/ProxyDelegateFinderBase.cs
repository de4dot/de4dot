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

namespace de4dot.code.deobfuscators {
	abstract class ProxyDelegateFinderBase {
		protected ModuleDefinition module;
		protected List<MethodDefinition> delegateCreatorMethods = new List<MethodDefinition>();
		Dictionary<TypeDefinition, bool> delegateTypesDict = new Dictionary<TypeDefinition, bool>();
		Dictionary<FieldReferenceAndDeclaringTypeKey, DelegateInfo> fieldToDelegateInfo = new Dictionary<FieldReferenceAndDeclaringTypeKey, DelegateInfo>();
		Dictionary<MethodDefinition, FieldDefinition> proxyMethodToField = new Dictionary<MethodDefinition, FieldDefinition>();
		int errors = 0;

		public int Errors {
			get { return errors; }
		}

		class DelegateInfo {
			public MethodReference methodRef;	// Method we should call
			public FieldDefinition field;		// Field holding the Delegate instance
			public OpCode callOpcode;
			public DelegateInfo(FieldDefinition field, MethodReference methodRef, OpCode callOpcode) {
				this.field = field;
				this.methodRef = methodRef;
				this.callOpcode = callOpcode;
			}
		}

		public int RemovedDelegateCreatorCalls { get; set; }

		public IEnumerable<TypeDefinition> DelegateTypes {
			get { return delegateTypesDict.Keys; }
		}

		public IEnumerable<TypeDefinition> DelegateCreatorTypes {
			get {
				foreach (var method in delegateCreatorMethods)
					yield return method.DeclaringType;
			}
		}

		public bool Detected {
			get { return delegateCreatorMethods.Count != 0; }
		}

		public ProxyDelegateFinderBase(ModuleDefinition module) {
			this.module = module;
		}

		public void setDelegateCreatorMethod(MethodDefinition delegateCreatorMethod) {
			if (delegateCreatorMethod == null)
				return;
			delegateCreatorMethods.Add(delegateCreatorMethod);
		}

		protected bool isDelegateCreatorMethod(MethodDefinition method) {
			foreach (var m in delegateCreatorMethods) {
				if (m == method)
					return true;
			}
			return false;
		}

		public void find() {
			if (delegateCreatorMethods.Count == 0)
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

				object context = checkCctor(type, cctor);
				if (context == null)
					continue;

				Log.v("Found proxy delegate: {0} ({1:X8})", type, type.MetadataToken.ToUInt32());
				RemovedDelegateCreatorCalls++;
				onFoundProxyDelegate(type);

				Log.indent();
				foreach (var field in type.Fields) {
					if (!field.IsStatic || field.IsPublic)
						continue;

					MethodReference calledMethod;
					OpCode callOpcode;
					getCallInfo(context, field, out calledMethod, out callOpcode);

					if (calledMethod == null)
						throw new ApplicationException("calledMethod is null");
					fieldToDelegateInfo[new FieldReferenceAndDeclaringTypeKey(field)] = new DelegateInfo(field, calledMethod, callOpcode);
					Log.v("Field: {0}, Opcode: {1}, Method: {2} ({3:X8})", field.Name, callOpcode, calledMethod, calledMethod.MetadataToken.ToUInt32());
				}
				Log.deIndent();
				delegateTypesDict[type] = true;
			}
		}

		protected virtual void onFoundProxyDelegate(TypeDefinition type) {
		}

		protected abstract object checkCctor(TypeDefinition type, MethodDefinition cctor);
		protected abstract void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode);

		protected void add(MethodDefinition proxyMethod, FieldDefinition proxyField) {
			if (proxyMethod == null || proxyField == null)
				return;
			proxyMethodToField[proxyMethod] = proxyField;
		}

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
					var instr = instrs[i];
					if (instr.OpCode == OpCodes.Ldsfld) {
						var di = getDelegateInfo(instr.Operand as FieldReference);
						if (di == null)
							continue;

						var visited = new Dictionary<Block, bool>();
						var callInfo = findProxyCall(di, block, i, visited, 1);
						if (callInfo != null) {
							add(removeInfos, block, i, null);
							add(removeInfos, callInfo.Block, callInfo.Index, di);
						}
						else {
							errors++;
							Log.w("Could not fix proxy call. Method: {0} ({1:X8}), Proxy type: {2} ({3:X8})",
								blocks.Method, blocks.Method.MetadataToken.ToInt32(),
								di.field.DeclaringType, di.field.DeclaringType.MetadataToken.ToInt32());
						}
					}
					else if (instr.OpCode == OpCodes.Call) {
						var method = instr.Operand as MethodDefinition;
						if (method == null)
							continue;
						FieldDefinition field;
						if (!proxyMethodToField.TryGetValue(method, out field))
							continue;
						var di = getDelegateInfo(field);
						if (di == null)
							continue;
						add(removeInfos, block, i, di);
					}
				}
			}

			foreach (var block in removeInfos.Keys) {
				var list = removeInfos[block];
				var removeIndexes = new List<int>(list.Count);
				foreach (var info in list) {
					if (info.IsCall) {
						var opcode = info.DelegateInfo.callOpcode;
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
				DotNetUtils.updateStack(instr.Instruction, ref stack, false);
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
