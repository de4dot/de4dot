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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	public abstract class ProxyCallFixerBase {
		protected ModuleDefMD module;
		protected List<MethodDef> delegateCreatorMethods = new List<MethodDef>();
		protected Dictionary<TypeDef, bool> delegateTypesDict = new Dictionary<TypeDef, bool>();
		protected int errors = 0;

		public int Errors => errors;

		protected class DelegateInfo {
			public IMethod methodRef;	// Method we should call
			public FieldDef field;		// Field holding the Delegate instance
			public OpCode callOpcode;
			public DelegateInfo(FieldDef field, IMethod methodRef, OpCode callOpcode) {
				this.field = field;
				this.methodRef = methodRef;
				this.callOpcode = callOpcode;
			}
		}

		public int RemovedDelegateCreatorCalls { get; set; }
		public IEnumerable<TypeDef> DelegateTypes => delegateTypesDict.Keys;

		public IEnumerable<TypeDef> DelegateCreatorTypes {
			get {
				foreach (var method in delegateCreatorMethods)
					yield return method.DeclaringType;
			}
		}

		public virtual IEnumerable<Tuple<MethodDef, string>> OtherMethods => new List<Tuple<MethodDef, string>>();
		public bool Detected => delegateCreatorMethods.Count != 0;
		protected ProxyCallFixerBase(ModuleDefMD module) => this.module = module;

		protected ProxyCallFixerBase(ModuleDefMD module, ProxyCallFixerBase oldOne) {
			this.module = module;
			foreach (var method in oldOne.delegateCreatorMethods)
				delegateCreatorMethods.Add(Lookup(method, "Could not find delegate creator method"));
			foreach (var kv in oldOne.delegateTypesDict)
				delegateTypesDict[Lookup(kv.Key, "Could not find delegate type")] = kv.Value;
		}

		protected DelegateInfo Copy(DelegateInfo di) {
			var method = Lookup(di.methodRef, "Could not find method ref");
			var field = Lookup(di.field, "Could not find delegate field");
			return new DelegateInfo(field, method, di.callOpcode);
		}

		protected T Lookup<T>(T def, string errorMessage) where T : class, ICodedToken =>
			DeobUtils.Lookup(module, def, errorMessage);

		protected void SetDelegateCreatorMethod(MethodDef delegateCreatorMethod) {
			if (delegateCreatorMethod == null)
				return;
			delegateCreatorMethods.Add(delegateCreatorMethod);
		}

		protected bool IsDelegateCreatorMethod(MethodDef method) {
			foreach (var m in delegateCreatorMethods) {
				if (m == method)
					return true;
			}
			return false;
		}

		protected virtual IEnumerable<TypeDef> GetDelegateTypes() {
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
			public bool IsCall => DelegateInfo != null;
		}

		protected virtual bool ProxyCallIsObfuscated => false;

		public void Deobfuscate(Blocks blocks) {
			if (blocks.Method.DeclaringType != null && delegateTypesDict.ContainsKey(blocks.Method.DeclaringType))
				return;
			var allBlocks = blocks.MethodBlocks.GetAllBlocks();
			int loops = ProxyCallIsObfuscated ? 50 : 1;
			for (int i = 0; i < loops; i++) {
				if (!Deobfuscate(blocks, allBlocks))
					break;
			}
			DeobfuscateEnd(blocks, allBlocks);
		}

		protected abstract bool Deobfuscate(Blocks blocks, IList<Block> allBlocks);

		protected virtual void DeobfuscateEnd(Blocks blocks, IList<Block> allBlocks) {
		}

		protected static void Add(Dictionary<Block, List<RemoveInfo>> removeInfos, Block block, int index, DelegateInfo di) {
			if (!removeInfos.TryGetValue(block, out var list))
				removeInfos[block] = list = new List<RemoveInfo>();
			list.Add(new RemoveInfo {
				Index = index,
				DelegateInfo = di,
			});
		}

		protected bool FixProxyCalls(MethodDef method, Dictionary<Block, List<RemoveInfo>> removeInfos) {
			var gpContext = GenericParamContext.Create(method);
			foreach (var block in removeInfos.Keys) {
				var list = removeInfos[block];
				var removeIndexes = new List<int>(list.Count);
				foreach (var info in list) {
					if (info.IsCall) {
						var opcode = info.DelegateInfo.callOpcode;
						var newInstr = Instruction.Create(opcode, ReResolve(info.DelegateInfo.methodRef, gpContext));
						block.Replace(info.Index, 1, newInstr);
					}
					else
						removeIndexes.Add(info.Index);
				}
				if (removeIndexes.Count > 0)
					block.Remove(removeIndexes);
			}

			return removeInfos.Count > 0;
		}

		IMethod ReResolve(IMethod method, GenericParamContext gpContext) {
			if (method.IsMethodSpec || method.IsMemberRef)
				method = module.ResolveToken(method.MDToken.Raw, gpContext) as IMethod ?? method;
			return method;
		}
	}

	// Fixes proxy calls that call the delegate inline in the code, eg.:
	//		ldsfld delegate_instance
	//		...push args...
	//		call Invoke
	public abstract class ProxyCallFixer1 : ProxyCallFixerBase {
		FieldDefAndDeclaringTypeDict<DelegateInfo> fieldToDelegateInfo = new FieldDefAndDeclaringTypeDict<DelegateInfo>();

		protected ProxyCallFixer1(ModuleDefMD module)
			: base(module) {
		}

		protected ProxyCallFixer1(ModuleDefMD module, ProxyCallFixer1 oldOne)
			: base(module, oldOne) {
			foreach (var key in oldOne.fieldToDelegateInfo.GetKeys())
				fieldToDelegateInfo.Add(Lookup(key, "Could not find field"), Copy(oldOne.fieldToDelegateInfo.Find(key)));
		}

		protected void AddDelegateInfo(DelegateInfo di) => fieldToDelegateInfo.Add(di.field, di);

		protected DelegateInfo GetDelegateInfo(IField field) {
			if (field == null)
				return null;
			return fieldToDelegateInfo.Find(field);
		}

		public void Find() {
			if (delegateCreatorMethods.Count == 0)
				return;

			Logger.v("Finding all proxy delegates");
			foreach (var tmp in GetDelegateTypes()) {
				var type = tmp;
				var cctor = type.FindStaticConstructor();
				if (cctor == null || !cctor.HasBody)
					continue;
				if (!type.HasFields)
					continue;

				object context = CheckCctor(ref type, cctor);
				if (context == null)
					continue;

				Logger.v("Found proxy delegate: {0} ({1:X8})", Utils.RemoveNewlines(type), type.MDToken.ToUInt32());
				RemovedDelegateCreatorCalls++;

				Logger.Instance.Indent();
				foreach (var field in type.Fields) {
					if (!field.IsStatic)
						continue;

					GetCallInfo(context, field, out var calledMethod, out var callOpcode);

					if (calledMethod == null)
						continue;
					AddDelegateInfo(new DelegateInfo(field, calledMethod, callOpcode));
					Logger.v("Field: {0}, Opcode: {1}, Method: {2} ({3:X8})",
								Utils.RemoveNewlines(field.Name),
								callOpcode,
								Utils.RemoveNewlines(calledMethod),
								calledMethod.MDToken.Raw);
				}
				Logger.Instance.DeIndent();
				delegateTypesDict[type] = true;
			}
		}

		protected abstract object CheckCctor(ref TypeDef type, MethodDef cctor);
		protected abstract void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode);

		protected override bool Deobfuscate(Blocks blocks, IList<Block> allBlocks) {
			var removeInfos = new Dictionary<Block, List<RemoveInfo>>();

			foreach (var block in allBlocks) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode != OpCodes.Ldsfld)
						continue;

					var di = GetDelegateInfo(instr.Operand as IField);
					if (di == null)
						continue;

					var callInfo = FindProxyCall(di, block, i);
					if (callInfo != null) {
						Add(removeInfos, block, i, null);
						Add(removeInfos, callInfo.Block, callInfo.Index, di);
					}
					else {
						errors++;
						Logger.w("Could not fix proxy call. Method: {0} ({1:X8}), Proxy type: {2} ({3:X8})",
							Utils.RemoveNewlines(blocks.Method),
							blocks.Method.MDToken.ToInt32(),
							Utils.RemoveNewlines(di.field.DeclaringType),
							di.field.DeclaringType.MDToken.ToInt32());
					}
				}
			}

			return FixProxyCalls(blocks.Method, removeInfos);
		}

		protected virtual BlockInstr FindProxyCall(DelegateInfo di, Block block, int index) =>
			FindProxyCall(di, block, index, new Dictionary<Block, bool>(), 1);

		BlockInstr FindProxyCall(DelegateInfo di, Block block, int index, Dictionary<Block, bool> visited, int stack) {
			if (visited.ContainsKey(block))
				return null;
			if (index <= 0)
				visited[block] = true;

			var instrs = block.Instructions;
			for (int i = index + 1; i < instrs.Count; i++) {
				if (stack <= 0)
					return null;
				var instr = instrs[i];
				instr.Instruction.UpdateStack(ref stack, false);
				if (stack < 0)
					return null;

				if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) {
					if (stack <= 0)
						return null;
					continue;
				}
				var calledMethod = instr.Operand as IMethod;
				if (calledMethod == null)
					return null;
				if (stack != (DotNetUtils.HasReturnValue(calledMethod) ? 1 : 0))
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

			foreach (var target in block.GetTargets()) {
				var info = FindProxyCall(di, target, -1, visited, stack);
				if (info != null)
					return info;
			}

			return null;
		}

		protected override void DeobfuscateEnd(Blocks blocks, IList<Block> allBlocks) =>
			FixBrokenCalls(blocks.Method, allBlocks);

		// The obfuscator could be buggy and call a proxy delegate without pushing the
		// instance field. SA has done it, so let's fix it.
		void FixBrokenCalls(MethodDef obfuscatedMethod, IList<Block> allBlocks) {
			foreach (var block in allBlocks) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var call = instrs[i];
					if (call.OpCode != OpCodes.Call && call.OpCode != OpCodes.Callvirt)
						continue;
					var methodRef = call.Operand as IMethod;
					if (methodRef == null || methodRef.Name != "Invoke")
						continue;
					var method = DotNetUtils.GetMethod2(module, methodRef);
					if (method == null || method.DeclaringType == null)
						continue;
					if (!delegateTypesDict.ContainsKey(method.DeclaringType))
						continue;

					// Oooops!!! The obfuscator is buggy. Well, let's hope it is, or it's my code. ;)

					Logger.w("Holy obfuscator bugs, Batman! Found a proxy delegate call with no instance push in {0:X8}. Replacing it with a throw...", obfuscatedMethod.MDToken.ToInt32());
					block.Insert(i, OpCodes.Ldnull.ToInstruction());
					block.Replace(i + 1, 1, OpCodes.Throw.ToInstruction());
					i++;
				}
			}
		}
	}

	// Fixes proxy calls that call a static method which then calls
	// Invoke() on a delegate instance, eg.:
	//		...push args...
	//		call static method
	public abstract class ProxyCallFixer2 : ProxyCallFixerBase {
		MethodDefAndDeclaringTypeDict<DelegateInfo> proxyMethodToDelegateInfo = new MethodDefAndDeclaringTypeDict<DelegateInfo>();

		protected ProxyCallFixer2(ModuleDefMD module)
			: base(module) {
		}

		protected ProxyCallFixer2(ModuleDefMD module, ProxyCallFixer2 oldOne)
			: base(module, oldOne) {
			foreach (var oldMethod in oldOne.proxyMethodToDelegateInfo.GetKeys()) {
				var oldDi = oldOne.proxyMethodToDelegateInfo.Find(oldMethod);
				var method = Lookup(oldMethod, "Could not find proxy method");
				proxyMethodToDelegateInfo.Add(method, Copy(oldDi));
			}
		}

		public void Find() {
			if (delegateCreatorMethods.Count == 0)
				return;

			Logger.v("Finding all proxy delegates");
			Find2();
		}

		protected void Find2() {
			foreach (var type in GetDelegateTypes()) {
				var cctor = type.FindStaticConstructor();
				if (cctor == null || !cctor.HasBody)
					continue;
				if (!type.HasFields)
					continue;

				object context = CheckCctor(type, cctor);
				if (context == null)
					continue;

				Logger.v("Found proxy delegate: {0} ({1:X8})", Utils.RemoveNewlines(type), type.MDToken.ToUInt32());
				RemovedDelegateCreatorCalls++;
				var fieldToMethod = GetFieldToMethodDictionary(type);

				Logger.Instance.Indent();
				foreach (var field in type.Fields) {
					if (!fieldToMethod.TryGetValue(field, out var proxyMethod))
						continue;

					GetCallInfo(context, field, out var calledMethod, out var callOpcode);

					if (calledMethod == null)
						continue;
					Add(proxyMethod, new DelegateInfo(field, calledMethod, callOpcode));
					Logger.v("Field: {0}, Opcode: {1}, Method: {2} ({3:X8})",
								Utils.RemoveNewlines(field.Name),
								callOpcode,
								Utils.RemoveNewlines(calledMethod),
								calledMethod.MDToken.ToUInt32());
				}
				Logger.Instance.DeIndent();
				delegateTypesDict[type] = true;
			}
		}

		protected void Add(MethodDef method, DelegateInfo di) => proxyMethodToDelegateInfo.Add(method, di);
		protected abstract object CheckCctor(TypeDef type, MethodDef cctor);
		protected abstract void GetCallInfo(object context, FieldDef field, out IMethod calledMethod, out OpCode callOpcode);

		Dictionary<FieldDef, MethodDef> GetFieldToMethodDictionary(TypeDef type) {
			var dict = new Dictionary<FieldDef, MethodDef>();
			foreach (var method in type.Methods) {
				if (!method.IsStatic || !method.HasBody || method.Name == ".cctor")
					continue;

				var instructions = method.Body.Instructions;
				for (int i = 0; i < instructions.Count; i++) {
					var instr = instructions[i];
					if (instr.OpCode.Code != Code.Ldsfld)
						continue;
					var field = instr.Operand as FieldDef;
					if (field == null)
						continue;

					dict[field] = method;
					break;
				}
			}
			return dict;
		}

		protected override bool Deobfuscate(Blocks blocks, IList<Block> allBlocks) {
			var removeInfos = new Dictionary<Block, List<RemoveInfo>>();

			foreach (var block in allBlocks) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode != OpCodes.Call)
						continue;

					var method = instr.Operand as IMethod;
					if (method == null)
						continue;
					var di = proxyMethodToDelegateInfo.Find(method);
					if (di == null)
						continue;
					Add(removeInfos, block, i, di);
				}
			}

			return FixProxyCalls(blocks.Method, removeInfos);
		}
	}

	// Fixes proxy calls that call a static method with the instance of
	// a delegate as the last arg, which then calls the Invoke method.
	//		...push args...
	//		ldsfld delegate instance
	//		call static method
	public abstract class ProxyCallFixer3 : ProxyCallFixer1 {
		protected ProxyCallFixer3(ModuleDefMD module)
			: base(module) {
		}

		protected ProxyCallFixer3(ModuleDefMD module, ProxyCallFixer3 oldOne)
			: base(module, oldOne) {
		}

		protected override BlockInstr FindProxyCall(DelegateInfo di, Block block, int index) {
			index++;
			if (index >= block.Instructions.Count)
				return null;
			var calledMethod = GetCalledMethod(block.Instructions[index]);
			if (calledMethod == null)
				return null;
			return new BlockInstr {
				Block = block,
				Index = index,
			};
		}

		static IMethod GetCalledMethod(Instr instr) {
			if (instr.OpCode.Code != Code.Call)
				return null;
			return instr.Operand as IMethod;
		}
	}
}
