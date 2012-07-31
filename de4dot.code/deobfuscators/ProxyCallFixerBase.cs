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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	abstract class ProxyCallFixerBase {
		protected ModuleDefinition module;
		protected List<MethodDefinition> delegateCreatorMethods = new List<MethodDefinition>();
		protected Dictionary<TypeDefinition, bool> delegateTypesDict = new Dictionary<TypeDefinition, bool>();
		protected int errors = 0;

		public int Errors {
			get { return errors; }
		}

		protected class DelegateInfo {
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

		public virtual IEnumerable<Tuple<MethodDefinition, string>> OtherMethods {
			get { return new List<Tuple<MethodDefinition, string>>(); }
		}

		public bool Detected {
			get { return delegateCreatorMethods.Count != 0; }
		}

		protected ProxyCallFixerBase(ModuleDefinition module) {
			this.module = module;
		}

		protected ProxyCallFixerBase(ModuleDefinition module, ProxyCallFixerBase oldOne) {
			this.module = module;
			foreach (var method in oldOne.delegateCreatorMethods)
				delegateCreatorMethods.Add(lookup(method, "Could not find delegate creator method"));
			foreach (var kv in oldOne.delegateTypesDict)
				delegateTypesDict[lookup(kv.Key, "Could not find delegate type")] = kv.Value;
		}

		protected DelegateInfo copy(DelegateInfo di) {
			var method = lookup(di.methodRef, "Could not find method ref");
			var field = lookup(di.field, "Could not find delegate field");
			return new DelegateInfo(field, method, di.callOpcode);
		}

		protected T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		protected void setDelegateCreatorMethod(MethodDefinition delegateCreatorMethod) {
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

		protected virtual IEnumerable<TypeDefinition> getDelegateTypes() {
			foreach (var type in module.Types) {
				if (type.BaseType == null || type.BaseType.FullName != "System.MulticastDelegate")
					continue;

				yield return type;
			}
		}

		protected class BlockInstr {
			public Block Block { get; set; }
			public int Index { get; set; }
		}

		protected class RemoveInfo {
			public int Index { get; set; }
			public DelegateInfo DelegateInfo { get; set; }
			public bool IsCall {
				get { return DelegateInfo != null; }
			}
		}

		protected virtual bool ProxyCallIsObfuscated {
			get { return false; }
		}

		public void deobfuscate(Blocks blocks) {
			if (blocks.Method.DeclaringType != null && delegateTypesDict.ContainsKey(blocks.Method.DeclaringType))
				return;
			var allBlocks = blocks.MethodBlocks.getAllBlocks();
			int loops = ProxyCallIsObfuscated ? 50 : 1;
			for (int i = 0; i < loops; i++) {
				if (!deobfuscate(blocks, allBlocks))
					break;
			}
			deobfuscateEnd(blocks, allBlocks);
		}

		protected abstract bool deobfuscate(Blocks blocks, IList<Block> allBlocks);

		protected virtual void deobfuscateEnd(Blocks blocks, IList<Block> allBlocks) {
		}

		protected static void add(Dictionary<Block, List<RemoveInfo>> removeInfos, Block block, int index, DelegateInfo di) {
			List<RemoveInfo> list;
			if (!removeInfos.TryGetValue(block, out list))
				removeInfos[block] = list = new List<RemoveInfo>();
			list.Add(new RemoveInfo {
				Index = index,
				DelegateInfo = di,
			});
		}

		protected static bool fixProxyCalls(Dictionary<Block, List<RemoveInfo>> removeInfos) {
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

			return removeInfos.Count > 0;
		}
	}

	// Fixes proxy calls that call the delegate inline in the code, eg.:
	//		ldsfld delegate_instance
	//		...push args...
	//		call Invoke
	abstract class ProxyCallFixer1 : ProxyCallFixerBase {
		FieldDefinitionAndDeclaringTypeDict<DelegateInfo> fieldToDelegateInfo = new FieldDefinitionAndDeclaringTypeDict<DelegateInfo>();

		protected ProxyCallFixer1(ModuleDefinition module)
			: base(module) {
		}

		protected ProxyCallFixer1(ModuleDefinition module, ProxyCallFixer1 oldOne)
			: base(module, oldOne) {
			foreach (var key in oldOne.fieldToDelegateInfo.getKeys())
				fieldToDelegateInfo.add(lookup(key, "Could not find field"), copy(oldOne.fieldToDelegateInfo.find(key)));
		}

		protected void addDelegateInfo(DelegateInfo di) {
			fieldToDelegateInfo.add(di.field, di);
		}

		protected DelegateInfo getDelegateInfo(FieldReference field) {
			if (field == null)
				return null;
			return fieldToDelegateInfo.find(field);
		}

		public void find() {
			if (delegateCreatorMethods.Count == 0)
				return;

			Log.v("Finding all proxy delegates");
			foreach (var tmp in getDelegateTypes()) {
				var type = tmp;
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null || !cctor.HasBody)
					continue;
				if (!type.HasFields)
					continue;

				object context = checkCctor(ref type, cctor);
				if (context == null)
					continue;

				Log.v("Found proxy delegate: {0} ({1:X8})", Utils.removeNewlines(type), type.MetadataToken.ToUInt32());
				RemovedDelegateCreatorCalls++;

				Log.indent();
				foreach (var field in type.Fields) {
					if (!field.IsStatic)
						continue;

					MethodReference calledMethod;
					OpCode callOpcode;
					getCallInfo(context, field, out calledMethod, out callOpcode);

					if (calledMethod == null)
						continue;
					addDelegateInfo(new DelegateInfo(field, calledMethod, callOpcode));
					Log.v("Field: {0}, Opcode: {1}, Method: {2} ({3:X8})",
								Utils.removeNewlines(field.Name),
								callOpcode,
								Utils.removeNewlines(calledMethod),
								calledMethod.MetadataToken.ToUInt32());
				}
				Log.deIndent();
				delegateTypesDict[type] = true;
			}
		}

		protected abstract object checkCctor(ref TypeDefinition type, MethodDefinition cctor);
		protected abstract void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode);

		protected override bool deobfuscate(Blocks blocks, IList<Block> allBlocks) {
			var removeInfos = new Dictionary<Block, List<RemoveInfo>>();

			foreach (var block in allBlocks) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode != OpCodes.Ldsfld)
						continue;

					var di = getDelegateInfo(instr.Operand as FieldReference);
					if (di == null)
						continue;

					var callInfo = findProxyCall(di, block, i);
					if (callInfo != null) {
						add(removeInfos, block, i, null);
						add(removeInfos, callInfo.Block, callInfo.Index, di);
					}
					else {
						errors++;
						Log.w("Could not fix proxy call. Method: {0} ({1:X8}), Proxy type: {2} ({3:X8})",
							Utils.removeNewlines(blocks.Method),
							blocks.Method.MetadataToken.ToInt32(),
							Utils.removeNewlines(di.field.DeclaringType),
							di.field.DeclaringType.MetadataToken.ToInt32());
					}
				}
			}

			return fixProxyCalls(removeInfos);
		}

		protected virtual BlockInstr findProxyCall(DelegateInfo di, Block block, int index) {
			return findProxyCall(di, block, index, new Dictionary<Block, bool>(), 1);
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

				if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) {
					if (stack <= 0)
						return null;
					continue;
				}
				var calledMethod = instr.Operand as MethodReference;
				if (calledMethod == null)
					return null;
				if (stack != (DotNetUtils.hasReturnValue(calledMethod) ? 1 : 0))
					continue;
				if (calledMethod.Name != "Invoke")
					return null;

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

		protected override void deobfuscateEnd(Blocks blocks, IList<Block> allBlocks) {
			fixBrokenCalls(blocks.Method, allBlocks);
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
					if (methodRef == null || methodRef.Name != "Invoke")
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

	// Fixes proxy calls that call a static method which then calls
	// Invoke() on a delegate instance, eg.:
	//		...push args...
	//		call static method
	abstract class ProxyCallFixer2 : ProxyCallFixerBase {
		MethodDefinitionAndDeclaringTypeDict<DelegateInfo> proxyMethodToDelegateInfo = new MethodDefinitionAndDeclaringTypeDict<DelegateInfo>();

		protected ProxyCallFixer2(ModuleDefinition module)
			: base(module) {
		}

		protected ProxyCallFixer2(ModuleDefinition module, ProxyCallFixer2 oldOne)
			: base(module, oldOne) {
			foreach (var oldMethod in oldOne.proxyMethodToDelegateInfo.getKeys()) {
				var oldDi = oldOne.proxyMethodToDelegateInfo.find(oldMethod);
				var method = lookup(oldMethod, "Could not find proxy method");
				proxyMethodToDelegateInfo.add(method, copy(oldDi));
			}
		}

		public void find() {
			if (delegateCreatorMethods.Count == 0)
				return;

			Log.v("Finding all proxy delegates");
			find2();
		}

		protected void find2() {
			foreach (var type in getDelegateTypes()) {
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null || !cctor.HasBody)
					continue;
				if (!type.HasFields)
					continue;

				object context = checkCctor(type, cctor);
				if (context == null)
					continue;

				Log.v("Found proxy delegate: {0} ({1:X8})", Utils.removeNewlines(type), type.MetadataToken.ToUInt32());
				RemovedDelegateCreatorCalls++;
				var fieldToMethod = getFieldToMethodDictionary(type);

				Log.indent();
				foreach (var field in type.Fields) {
					MethodDefinition proxyMethod;
					if (!fieldToMethod.TryGetValue(field, out proxyMethod))
						continue;

					MethodReference calledMethod;
					OpCode callOpcode;
					getCallInfo(context, field, out calledMethod, out callOpcode);

					if (calledMethod == null)
						continue;
					add(proxyMethod, new DelegateInfo(field, calledMethod, callOpcode));
					Log.v("Field: {0}, Opcode: {1}, Method: {2} ({3:X8})",
								Utils.removeNewlines(field.Name),
								callOpcode,
								Utils.removeNewlines(calledMethod),
								calledMethod.MetadataToken.ToUInt32());
				}
				Log.deIndent();
				delegateTypesDict[type] = true;
			}
		}

		protected void add(MethodDefinition method, DelegateInfo di) {
			proxyMethodToDelegateInfo.add(method, di);
		}

		protected abstract object checkCctor(TypeDefinition type, MethodDefinition cctor);
		protected abstract void getCallInfo(object context, FieldDefinition field, out MethodReference calledMethod, out OpCode callOpcode);

		Dictionary<FieldDefinition, MethodDefinition> getFieldToMethodDictionary(TypeDefinition type) {
			var dict = new Dictionary<FieldDefinition, MethodDefinition>();
			foreach (var method in type.Methods) {
				if (!method.IsStatic || !method.HasBody || method.Name == ".cctor")
					continue;

				var instructions = method.Body.Instructions;
				for (int i = 0; i < instructions.Count; i++) {
					var instr = instructions[i];
					if (instr.OpCode.Code != Code.Ldsfld)
						continue;
					var field = instr.Operand as FieldDefinition;
					if (field == null)
						continue;

					dict[field] = method;
					break;
				}
			}
			return dict;
		}

		protected override bool deobfuscate(Blocks blocks, IList<Block> allBlocks) {
			var removeInfos = new Dictionary<Block, List<RemoveInfo>>();

			foreach (var block in allBlocks) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode != OpCodes.Call)
						continue;

					var method = instr.Operand as MethodReference;
					if (method == null)
						continue;
					var di = proxyMethodToDelegateInfo.find(method);
					if (di == null)
						continue;
					add(removeInfos, block, i, di);
				}
			}

			return fixProxyCalls(removeInfos);
		}
	}

	// Fixes proxy calls that call a static method with the instance of
	// a delegate as the last arg, which then calls the Invoke method.
	//		...push args...
	//		ldsfld delegate instance
	//		call static method
	abstract class ProxyCallFixer3 : ProxyCallFixer1 {
		protected ProxyCallFixer3(ModuleDefinition module)
			: base(module) {
		}

		protected ProxyCallFixer3(ModuleDefinition module, ProxyCallFixer3 oldOne)
			: base(module, oldOne) {
		}

		protected override BlockInstr findProxyCall(DelegateInfo di, Block block, int index) {
			index++;
			if (index >= block.Instructions.Count)
				return null;
			var calledMethod = getCalledMethod(block.Instructions[index]);
			if (calledMethod == null)
				return null;
			return new BlockInstr {
				Block = block,
				Index = index,
			};
		}

		static MethodReference getCalledMethod(Instr instr) {
			if (instr.OpCode.Code != Code.Call)
				return null;
			return instr.Operand as MethodReference;
		}
	}
}
