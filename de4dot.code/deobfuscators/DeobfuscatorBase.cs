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
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.blocks.cflow;
using de4dot.PE;

namespace de4dot.code.deobfuscators {
	abstract class DeobfuscatorBase : IDeobfuscator, IWriterListener {
		public const string DEFAULT_VALID_NAME_REGEX = @"^[a-zA-Z_<{$][a-zA-Z_0-9<>{}$.`-]*$";

		class RemoveInfo<T> {
			public T obj;
			public string reason;
			public RemoveInfo(T obj, string reason) {
				this.obj = obj;
				this.reason = reason;
			}
		}

		OptionsBase optionsBase;
		protected ModuleDefinition module;
		protected StaticStringInliner staticStringInliner = new StaticStringInliner();
		IList<RemoveInfo<TypeDefinition>> typesToRemove = new List<RemoveInfo<TypeDefinition>>();
		IList<RemoveInfo<MethodDefinition>> methodsToRemove = new List<RemoveInfo<MethodDefinition>>();
		IList<RemoveInfo<FieldDefinition>> fieldsToRemove = new List<RemoveInfo<FieldDefinition>>();
		IList<RemoveInfo<TypeDefinition>> attrsToRemove = new List<RemoveInfo<TypeDefinition>>();
		IList<RemoveInfo<Resource>> resourcesToRemove = new List<RemoveInfo<Resource>>();
		IList<RemoveInfo<ModuleReference>> modrefsToRemove = new List<RemoveInfo<ModuleReference>>();
		IList<RemoveInfo<AssemblyNameReference>> asmrefsToRemove = new List<RemoveInfo<AssemblyNameReference>>();
		List<string> namesToPossiblyRemove = new List<string>();
		MethodCallRemover methodCallRemover = new MethodCallRemover();
		byte[] moduleBytes;
		protected InitializedDataCreator initializedDataCreator;

		protected byte[] ModuleBytes {
			get { return moduleBytes; }
			set { moduleBytes = value; }
		}

		internal class OptionsBase : IDeobfuscatorOptions {
			public bool RenameResourcesInCode { get; set; }
			public NameRegexes ValidNameRegex { get; set; }
			public bool DecryptStrings { get; set; }

			public OptionsBase() {
				RenameResourcesInCode = true;
			}
		}

		public IDeobfuscatorOptions TheOptions {
			get { return optionsBase; }
		}

		public IOperations Operations { get; set; }
		public IDeobfuscatedFile DeobfuscatedFile { get; set; }
		public virtual StringFeatures StringFeatures { get; set; }
		public virtual RenamingOptions RenamingOptions { get; set; }
		public DecrypterType DefaultDecrypterType { get; set; }

		public abstract string Type { get; }
		public abstract string TypeLong { get; }
		public abstract string Name { get; }

		protected virtual bool CanInlineMethods {
			get { return false; }
		}

		protected virtual bool KeepTypes {
			get { return false; }
		}

		protected bool CanRemoveStringDecrypterType {
			get { return Operations.DecryptStrings != OpDecryptString.None && staticStringInliner.InlinedAllCalls; }
		}

		public virtual IEnumerable<IBlocksDeobfuscator> BlocksDeobfuscators {
			get {
				var list = new List<IBlocksDeobfuscator>();
				if (CanInlineMethods)
					list.Add(new MethodCallInliner(false));
				return list;
			}
		}

		public DeobfuscatorBase(OptionsBase optionsBase) {
			this.optionsBase = optionsBase;
			StringFeatures = StringFeatures.AllowAll;
			DefaultDecrypterType = DecrypterType.Static;
		}

		public virtual byte[] unpackNativeFile(PeImage peImage) {
			return null;
		}

		public virtual void init(ModuleDefinition module) {
			setModule(module);
		}

		protected void setModule(ModuleDefinition module) {
			this.module = module;
			initializedDataCreator = new InitializedDataCreator(module);
		}

		protected virtual bool checkValidName(string name) {
			return optionsBase.ValidNameRegex.isMatch(name);
		}

		public virtual int detect() {
			scanForObfuscator();
			return detectInternal();
		}

		protected abstract void scanForObfuscator();
		protected abstract int detectInternal();

		public virtual bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			return false;
		}

		public virtual IDeobfuscator moduleReloaded(ModuleDefinition module) {
			throw new ApplicationException("moduleReloaded() must be overridden by the deobfuscator");
		}

		public virtual void deobfuscateBegin() {
			ModuleBytes = null;
		}

		public virtual void deobfuscateMethodBegin(Blocks blocks) {
		}

		public virtual void deobfuscateMethodEnd(Blocks blocks) {
			removeMethodCalls(blocks);
		}

		public virtual void deobfuscateStrings(Blocks blocks) {
			staticStringInliner.decrypt(blocks);
		}

		public virtual bool deobfuscateOther(Blocks blocks) {
			return false;
		}

		public virtual void deobfuscateEnd() {
			if (!Operations.KeepObfuscatorTypes && !KeepTypes) {
				removeTypesWithInvalidBaseTypes();

				deleteEmptyCctors();
				deleteMethods();
				deleteFields();
				deleteCustomAttributes();
				deleteOtherAttributes();

				// Delete types after removing methods, fields, and attributes. The reason is
				// that the Scope property will be null if we remove a type. Comparing a
				// typeref with a typedef will then fail.
				deleteTypes();

				deleteDllResources();
				deleteModuleReferences();
				deleteAssemblyReferences();
			}

			restoreBaseType();
		}

		static bool isTypeWithInvalidBaseType(TypeDefinition moduleType, TypeDefinition type) {
			return type.BaseType == null && !type.IsInterface && type != moduleType;
		}

		void restoreBaseType() {
			var moduleType = DotNetUtils.getModuleType(module);
			foreach (var type in module.GetTypes()) {
				if (!isTypeWithInvalidBaseType(moduleType, type))
					continue;
				Log.v("Adding System.Object as base type: {0} ({1:X8})",
							Utils.removeNewlines(type),
							type.MetadataToken.ToInt32());
				type.BaseType = module.TypeSystem.Object;
			}
		}

		void removeTypesWithInvalidBaseTypes() {
			var moduleType = DotNetUtils.getModuleType(module);
			foreach (var type in module.GetTypes()) {
				if (!isTypeWithInvalidBaseType(moduleType, type))
					continue;
				addTypeToBeRemoved(type, "Invalid type with no base type (anti-reflection)");
			}
		}

		protected void fixEnumTypes() {
			foreach (var type in module.GetTypes()) {
				if (!type.IsEnum)
					continue;
				foreach (var field in type.Fields) {
					if (field.IsStatic)
						continue;
					field.IsRuntimeSpecialName = true;
					field.IsSpecialName = true;
				}
			}
		}

		protected void fixInterfaces() {
			foreach (var type in module.GetTypes()) {
				if (!type.IsInterface)
					continue;
				type.IsSealed = false;
			}
		}

		public abstract IEnumerable<int> getStringDecrypterMethods();

		class MethodCallRemover {
			Dictionary<string, MethodDefinitionAndDeclaringTypeDict<bool>> methodNameInfos = new Dictionary<string, MethodDefinitionAndDeclaringTypeDict<bool>>();
			MethodDefinitionAndDeclaringTypeDict<MethodDefinitionAndDeclaringTypeDict<bool>> methodRefInfos = new MethodDefinitionAndDeclaringTypeDict<MethodDefinitionAndDeclaringTypeDict<bool>>();

			void checkMethod(MethodReference methodToBeRemoved) {
				if (methodToBeRemoved.Parameters.Count != 0)
					throw new ApplicationException(string.Format("Method takes params: {0}", methodToBeRemoved));
				if (DotNetUtils.hasReturnValue(methodToBeRemoved))
					throw new ApplicationException(string.Format("Method has a return value: {0}", methodToBeRemoved));
			}

			public void add(string method, MethodDefinition methodToBeRemoved) {
				if (methodToBeRemoved == null)
					return;
				checkMethod(methodToBeRemoved);

				MethodDefinitionAndDeclaringTypeDict<bool> dict;
				if (!methodNameInfos.TryGetValue(method, out dict))
					methodNameInfos[method] = dict = new MethodDefinitionAndDeclaringTypeDict<bool>();
				dict.add(methodToBeRemoved, true);
			}

			public void add(MethodDefinition method, MethodDefinition methodToBeRemoved) {
				if (method == null || methodToBeRemoved == null)
					return;
				checkMethod(methodToBeRemoved);

				var dict = methodRefInfos.find(method);
				if (dict == null)
					methodRefInfos.add(method, dict = new MethodDefinitionAndDeclaringTypeDict<bool>());
				dict.add(methodToBeRemoved, true);
			}

			public void removeAll(Blocks blocks) {
				var allBlocks = blocks.MethodBlocks.getAllBlocks();

				removeAll(allBlocks, blocks, blocks.Method.Name);
				removeAll(allBlocks, blocks, blocks.Method);
			}

			void removeAll(IList<Block> allBlocks, Blocks blocks, string method) {
				MethodDefinitionAndDeclaringTypeDict<bool> info;
				if (!methodNameInfos.TryGetValue(method, out info))
					return;

				removeCalls(allBlocks, blocks, info);
			}

			void removeAll(IList<Block> allBlocks, Blocks blocks, MethodDefinition method) {
				var info = methodRefInfos.find(method);
				if (info == null)
					return;

				removeCalls(allBlocks, blocks, info);
			}

			void removeCalls(IList<Block> allBlocks, Blocks blocks, MethodDefinitionAndDeclaringTypeDict<bool> info) {
				var instrsToDelete = new List<int>();
				foreach (var block in allBlocks) {
					instrsToDelete.Clear();
					for (int i = 0; i < block.Instructions.Count; i++) {
						var instr = block.Instructions[i];
						if (instr.OpCode != OpCodes.Call)
							continue;
						var destMethod = instr.Operand as MethodReference;
						if (destMethod == null)
							continue;

						if (info.find(destMethod)) {
							Log.v("Removed call to {0}", Utils.removeNewlines(destMethod));
							instrsToDelete.Add(i);
						}
					}
					block.remove(instrsToDelete);
				}
			}
		}

		public void addCctorInitCallToBeRemoved(MethodDefinition methodToBeRemoved) {
			methodCallRemover.add(".cctor", methodToBeRemoved);
		}

		public void addModuleCctorInitCallToBeRemoved(MethodDefinition methodToBeRemoved) {
			methodCallRemover.add(DotNetUtils.getModuleTypeCctor(module), methodToBeRemoved);
		}

		public void addCtorInitCallToBeRemoved(MethodDefinition methodToBeRemoved) {
			methodCallRemover.add(".ctor", methodToBeRemoved);
		}

		public void addCallToBeRemoved(MethodDefinition method, MethodDefinition methodToBeRemoved) {
			methodCallRemover.add(method, methodToBeRemoved);
		}

		void removeMethodCalls(Blocks blocks) {
			methodCallRemover.removeAll(blocks);
		}

		protected void addMethodsToBeRemoved(IEnumerable<MethodDefinition> methods, string reason) {
			foreach (var method in methods)
				addMethodToBeRemoved(method, reason);
		}

		protected void addMethodToBeRemoved(MethodDefinition method, string reason) {
			if (method != null)
				methodsToRemove.Add(new RemoveInfo<MethodDefinition>(method, reason));
		}

		protected void addFieldsToBeRemoved(IEnumerable<FieldDefinition> fields, string reason) {
			foreach (var field in fields)
				addFieldToBeRemoved(field, reason);
		}

		protected void addFieldToBeRemoved(FieldDefinition field, string reason) {
			if (field != null)
				fieldsToRemove.Add(new RemoveInfo<FieldDefinition>(field, reason));
		}

		protected void addAttributesToBeRemoved(IEnumerable<TypeDefinition> attrs, string reason) {
			foreach (var attr in attrs)
				addAttributeToBeRemoved(attr, reason);
		}

		protected void addAttributeToBeRemoved(TypeDefinition attr, string reason) {
			if (attr == null)
				return;
			addTypeToBeRemoved(attr, reason);
			attrsToRemove.Add(new RemoveInfo<TypeDefinition>(attr, reason));
		}

		protected void addTypesToBeRemoved(IEnumerable<TypeDefinition> types, string reason) {
			foreach (var type in types)
				addTypeToBeRemoved(type, reason);
		}

		protected void addTypeToBeRemoved(TypeDefinition type, string reason) {
			if (type != null)
				typesToRemove.Add(new RemoveInfo<TypeDefinition>(type, reason));
		}

		protected void addResourceToBeRemoved(Resource resource, string reason) {
			if (resource != null)
				resourcesToRemove.Add(new RemoveInfo<Resource>(resource, reason));
		}

		protected void addModuleReferencesToBeRemoved(IEnumerable<ModuleReference> modrefs, string reason) {
			foreach (var modref in modrefs)
				addModuleReferenceToBeRemoved(modref, reason);
		}

		protected void addModuleReferenceToBeRemoved(ModuleReference modref, string reason) {
			if (modref != null)
				modrefsToRemove.Add(new RemoveInfo<ModuleReference>(modref, reason));
		}

		protected void addAssemblyReferenceToBeRemoved(AssemblyNameReference asmRef, string reason) {
			if (asmRef != null)
				asmrefsToRemove.Add(new RemoveInfo<AssemblyNameReference>(asmRef, reason));
		}

		void deleteEmptyCctors() {
			var emptyCctorsToRemove = new List<MethodDefinition>();
			foreach (var type in module.GetTypes()) {
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor != null && DotNetUtils.isEmpty(cctor))
					emptyCctorsToRemove.Add(cctor);
			}

			if (emptyCctorsToRemove.Count == 0)
				return;

			Log.v("Removing empty .cctor methods");
			Log.indent();
			foreach (var cctor in emptyCctorsToRemove) {
				var type = cctor.DeclaringType;
				if (type == null)
					continue;
				if (type.Methods.Remove(cctor))
					Log.v("{0:X8}, type: {1} ({2:X8})",
								cctor.MetadataToken.ToUInt32(),
								Utils.removeNewlines(type),
								type.MetadataToken.ToUInt32());
			}
			Log.deIndent();
		}

		void deleteMethods() {
			if (methodsToRemove.Count == 0)
				return;

			Log.v("Removing methods");
			Log.indent();
			foreach (var info in methodsToRemove) {
				var method = info.obj;
				if (method == null)
					continue;
				var type = method.DeclaringType;
				if (type == null)
					continue;
				if (type.Methods.Remove(method))
					Log.v("Removed method {0} ({1:X8}) (Type: {2}) (reason: {3})",
								Utils.removeNewlines(method),
								method.MetadataToken.ToUInt32(),
								Utils.removeNewlines(type),
								info.reason);
			}
			Log.deIndent();
		}

		void deleteFields() {
			if (fieldsToRemove.Count == 0)
				return;

			Log.v("Removing fields");
			Log.indent();
			foreach (var info in fieldsToRemove) {
				var field = info.obj;
				if (field == null)
					continue;
				var type = field.DeclaringType;
				if (type == null)
					continue;
				if (type.Fields.Remove(field))
					Log.v("Removed field {0} ({1:X8}) (Type: {2}) (reason: {3})",
								Utils.removeNewlines(field),
								field.MetadataToken.ToUInt32(),
								Utils.removeNewlines(type),
								info.reason);
			}
			Log.deIndent();
		}

		void deleteTypes() {
			var types = module.Types;
			if (types == null || typesToRemove.Count == 0)
				return;

			Log.v("Removing types");
			Log.indent();
			var moduleType = DotNetUtils.getModuleType(module);
			foreach (var info in typesToRemove) {
				var typeDef = info.obj;
				if (typeDef == null || typeDef == moduleType)
					continue;
				bool removed;
				if (typeDef.IsNested)
					removed = typeDef.DeclaringType.NestedTypes.Remove(typeDef);
				else
					removed = types.Remove(typeDef);
				if (removed)
					Log.v("Removed type {0} ({1:X8}) (reason: {2})",
								Utils.removeNewlines(typeDef),
								typeDef.MetadataToken.ToUInt32(),
								info.reason);
			}
			Log.deIndent();
		}

		void deleteCustomAttributes() {
			if (attrsToRemove.Count == 0)
				return;

			Log.v("Removing custom attributes");
			Log.indent();
			deleteCustomAttributes(module.CustomAttributes);
			if (module.Assembly != null)
				deleteCustomAttributes(module.Assembly.CustomAttributes);
			Log.deIndent();
		}

		void deleteCustomAttributes(IList<CustomAttribute> customAttrs) {
			if (customAttrs == null)
				return;
			foreach (var info in attrsToRemove) {
				var typeDef = info.obj;
				if (typeDef == null)
					continue;
				for (int i = 0; i < customAttrs.Count; i++) {
					if (MemberReferenceHelper.compareTypes(customAttrs[i].AttributeType, typeDef)) {
						customAttrs.RemoveAt(i);
						Log.v("Removed custom attribute {0} ({1:X8}) (reason: {2})",
									Utils.removeNewlines(typeDef),
									typeDef.MetadataToken.ToUInt32(),
									info.reason);
						break;
					}
				}
			}
		}

		void deleteOtherAttributes() {
			Log.v("Removing other attributes");
			Log.indent();
			deleteOtherAttributes(module.CustomAttributes);
			if (module.Assembly != null)
				deleteOtherAttributes(module.Assembly.CustomAttributes);
			Log.deIndent();
		}

		void deleteOtherAttributes(IList<CustomAttribute> customAttributes) {
			for (int i = customAttributes.Count - 1; i >= 0; i--) {
				var attr = customAttributes[i].AttributeType;
				if (attr.FullName == "System.Runtime.CompilerServices.SuppressIldasmAttribute") {
					Log.v("Removed attribute {0}", Utils.removeNewlines(attr.FullName));
					customAttributes.RemoveAt(i);
				}
			}
		}

		void deleteDllResources() {
			if (!module.HasResources || resourcesToRemove.Count == 0)
				return;

			Log.v("Removing resources");
			Log.indent();
			foreach (var info in resourcesToRemove) {
				var resource = info.obj;
				if (resource == null)
					continue;
				if (module.Resources.Remove(resource))
					Log.v("Removed resource {0} (reason: {1})", Utils.toCsharpString(resource.Name), info.reason);
			}
			Log.deIndent();
		}

		void deleteModuleReferences() {
			if (!module.HasModuleReferences || modrefsToRemove.Count == 0)
				return;

			Log.v("Removing module references");
			Log.indent();
			foreach (var info in modrefsToRemove) {
				var modref = info.obj;
				if (modref == null)
					continue;
				if (module.ModuleReferences.Remove(modref))
					Log.v("Removed module reference {0} (reason: {1})", modref, info.reason);
			}
			Log.deIndent();
		}

		void deleteAssemblyReferences() {
			if (!module.HasAssemblyReferences || asmrefsToRemove.Count == 0)
				return;

			Log.v("Removing assembly references");
			Log.indent();
			foreach (var info in asmrefsToRemove) {
				var asmRef = info.obj;
				if (asmRef == null)
					continue;
				if (module.AssemblyReferences.Remove(asmRef))
					Log.v("Removed assembly reference {0} (reason: {1})", asmRef, info.reason);
			}
			Log.deIndent();
		}

		protected void setInitLocals() {
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (isFatHeader(method))
						method.Body.InitLocals = true;
				}
			}
		}

		static bool isFatHeader(MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;
			var body = method.Body;
			if (body.InitLocals || body.MaxStackSize > 8)
				return true;
			if (body.Variables.Count > 0)
				return true;
			if (body.ExceptionHandlers.Count > 0)
				return true;
			if (getCodeSize(method) > 63)
				return true;

			return false;
		}

		static int getCodeSize(MethodDefinition method) {
			if (method == null || method.Body == null)
				return 0;
			int size = 0;
			foreach (var instr in method.Body.Instructions)
				size += instr.GetSize();
			return size;
		}

		public override string ToString() {
			return Name;
		}

		protected void findPossibleNamesToRemove(MethodDefinition method) {
			if (method == null || !method.HasBody)
				return;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode == OpCodes.Ldstr)
					namesToPossiblyRemove.Add((string)instr.Operand);
			}
		}

		protected void addResources(string reason) {
			if (!module.HasResources)
				return;

			foreach (var name in namesToPossiblyRemove) {
				foreach (var resource in module.Resources) {
					if (resource.Name == name) {
						addResourceToBeRemoved(resource, reason);
						break;
					}
				}
			}
		}

		protected void addModuleReferences(string reason) {
			if (!module.HasModuleReferences)
				return;

			foreach (var name in namesToPossiblyRemove) {
				foreach (var moduleRef in module.ModuleReferences) {
					if (Utils.StartsWith(moduleRef.Name, name, StringComparison.OrdinalIgnoreCase))
						addModuleReferenceToBeRemoved(moduleRef, reason);
				}
			}
		}

		protected bool removeProxyDelegates(ProxyCallFixerBase proxyCallFixer) {
			return removeProxyDelegates(proxyCallFixer, true);
		}

		protected bool removeProxyDelegates(ProxyCallFixerBase proxyCallFixer, bool removeCreators) {
			if (proxyCallFixer.Errors != 0) {
				Log.v("Not removing proxy delegates and creator type since errors were detected.");
				return false;
			}
			addTypesToBeRemoved(proxyCallFixer.DelegateTypes, "Proxy delegate type");
			if (removeCreators && proxyCallFixer.RemovedDelegateCreatorCalls > 0) {
				addTypesToBeRemoved(proxyCallFixer.DelegateCreatorTypes, "Proxy delegate creator type");
				foreach (var tuple in proxyCallFixer.OtherMethods)
					addMethodToBeRemoved(tuple.Item1, tuple.Item2);
			}
			return true;
		}

		protected Resource getResource(IEnumerable<string> strings) {
			return DotNetUtils.getResource(module, strings);
		}

		protected CustomAttribute getAssemblyAttribute(TypeReference attr) {
			var list = new List<CustomAttribute>(DotNetUtils.findAttributes(module.Assembly, attr));
			return list.Count == 0 ? null : list[0];
		}

		protected CustomAttribute getModuleAttribute(TypeReference attr) {
			var list = new List<CustomAttribute>(DotNetUtils.findAttributes(module, attr));
			return list.Count == 0 ? null : list[0];
		}

		protected bool hasMetadataStream(string name) {
			foreach (var stream in module.MetadataStreams) {
				if (stream.Name == name)
					return true;
			}
			return false;
		}

		List<T> getObjectsToRemove<T>(IList<RemoveInfo<T>> removeThese) where T : MemberReference {
			var list = new List<T>(removeThese.Count);
			foreach (var info in removeThese) {
				if (info.obj != null)
					list.Add(info.obj);
			}
			return list;
		}

		protected List<TypeDefinition> getTypesToRemove() {
			return getObjectsToRemove(typesToRemove);
		}

		protected List<MethodDefinition> getMethodsToRemove() {
			return getObjectsToRemove(methodsToRemove);
		}

		public virtual bool isValidNamespaceName(string ns) {
			if (ns == null)
				return false;
			foreach (var part in ns.Split(new char[] { '.' })) {
				if (!checkValidName(part))
					return false;
			}
			return true;
		}

		public virtual bool isValidTypeName(string name) {
			return name != null && checkValidName(name);
		}

		public virtual bool isValidMethodName(string name) {
			return name != null && checkValidName(name);
		}

		public virtual bool isValidPropertyName(string name) {
			return name != null && checkValidName(name);
		}

		public virtual bool isValidEventName(string name) {
			return name != null && checkValidName(name);
		}

		public virtual bool isValidFieldName(string name) {
			return name != null && checkValidName(name);
		}

		public virtual bool isValidGenericParamName(string name) {
			return name != null && checkValidName(name);
		}

		public virtual bool isValidMethodArgName(string name) {
			return name != null && checkValidName(name);
		}

		public virtual bool isValidResourceKeyName(string name) {
			return name != null && checkValidName(name);
		}

		public virtual void OnBeforeAddingResources(MetadataBuilder builder) {
		}

		protected void findAndRemoveInlinedMethods() {
			removeInlinedMethods(InlinedMethodsFinder.find(module));
		}

		protected void removeInlinedMethods(List<MethodDefinition> inlinedMethods) {
			addMethodsToBeRemoved(new UnusedMethodsFinder(module, inlinedMethods, getRemovedMethods()).find(), "Inlined method");
		}

		protected MethodCollection getRemovedMethods() {
			var removedMethods = new MethodCollection();
			removedMethods.add(getMethodsToRemove());
			removedMethods.addAndNested(getTypesToRemove());
			return removedMethods;
		}

		protected bool isTypeCalled(TypeDefinition decrypterType) {
			if (decrypterType == null)
				return false;

			var decrypterMethods = new MethodCollection();
			decrypterMethods.addAndNested(decrypterType);

			var removedMethods = getRemovedMethods();

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method.Body == null)
						continue;
					if (decrypterMethods.exists(method))
						break;	// decrypter type / nested type method
					if (removedMethods.exists(method))
						continue;

					foreach (var instr in method.Body.Instructions) {
						switch (instr.OpCode.Code) {
						case Code.Call:
						case Code.Callvirt:
						case Code.Newobj:
							var calledMethod = instr.Operand as MethodReference;
							if (calledMethod == null)
								break;
							if (decrypterMethods.exists(calledMethod))
								return true;
							break;

						default:
							break;
						}
					}
				}
			}

			return false;
		}

		public static int toInt32(bool b) {
			return b ? 1 : 0;
		}
	}
}
