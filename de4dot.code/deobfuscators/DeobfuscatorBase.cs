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
	abstract class DeobfuscatorBase : IDeobfuscator {
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
		protected StaticStringDecrypter staticStringDecrypter = new StaticStringDecrypter();
		IList<RemoveInfo<TypeDefinition>> typesToRemove = new List<RemoveInfo<TypeDefinition>>();
		IList<RemoveInfo<MethodDefinition>> methodsToRemove = new List<RemoveInfo<MethodDefinition>>();
		IList<RemoveInfo<FieldDefinition>> fieldsToRemove = new List<RemoveInfo<FieldDefinition>>();
		IList<RemoveInfo<TypeDefinition>> attrsToRemove = new List<RemoveInfo<TypeDefinition>>();
		IList<RemoveInfo<Resource>> resourcesToRemove = new List<RemoveInfo<Resource>>();
		IList<RemoveInfo<ModuleReference>> modrefsToRemove = new List<RemoveInfo<ModuleReference>>();
		List<string> namesToPossiblyRemove = new List<string>();
		bool scanForObfuscatorCalled = false;
		MethodCallRemover methodCallRemover = new MethodCallRemover();

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
		public DecrypterType DefaultDecrypterType { get; set; }

		public abstract string Type { get; }
		public abstract string Name { get; }

		public Func<string, bool> IsValidName {
			get { return (name) => optionsBase.ValidNameRegex.isMatch(name); }
		}

		public DeobfuscatorBase(OptionsBase optionsBase) {
			this.optionsBase = optionsBase;
			StringFeatures = StringFeatures.AllowAll;
			DefaultDecrypterType = DecrypterType.Static;
		}

		public virtual void init(ModuleDefinition module) {
			this.module = module;
		}

		public virtual int earlyDetect() {
			return 0;
		}

		protected void scanForObfuscator() {
			if (scanForObfuscatorCalled)
				return;
			scanForObfuscatorCalled = true;
			scanForObfuscatorInternal();
		}

		protected virtual void scanForObfuscatorInternal() {
		}

		public abstract int detect();

		public virtual void deobfuscateBegin() {
			scanForObfuscator();
		}

		public virtual void deobfuscateMethodBegin(Blocks blocks) {
		}

		public virtual void deobfuscateMethodEnd(Blocks blocks) {
			removeMethodCalls(blocks);
		}

		public virtual void deobfuscateStrings(Blocks blocks) {
			if (staticStringDecrypter.HasHandlers)
				staticStringDecrypter.decrypt(blocks);
		}

		public virtual void deobfuscateEnd() {
			if (!Operations.KeepObfuscatorTypes) {
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
			}
		}

		public virtual IEnumerable<string> getStringDecrypterMethods() {
			return new List<string>();
		}

		class MethodCallRemover {
			Dictionary<string, Dictionary<MethodReferenceAndDeclaringTypeKey, bool>> methodNameInfos = new Dictionary<string, Dictionary<MethodReferenceAndDeclaringTypeKey, bool>>(StringComparer.Ordinal);
			Dictionary<MethodReferenceAndDeclaringTypeKey, Dictionary<MethodReferenceAndDeclaringTypeKey, bool>> methodRefInfos = new Dictionary<MethodReferenceAndDeclaringTypeKey, Dictionary<MethodReferenceAndDeclaringTypeKey, bool>>();

			void checkMethod(MethodReference methodToBeRemoved) {
				if (methodToBeRemoved.Parameters.Count != 0)
					throw new ApplicationException(string.Format("Method takes params: {0}", methodToBeRemoved));
				if (DotNetUtils.hasReturnValue(methodToBeRemoved))
					throw new ApplicationException(string.Format("Method has a return value: {0}", methodToBeRemoved));
			}

			public void add(string method, MethodReference methodToBeRemoved) {
				if (methodToBeRemoved == null)
					return;
				checkMethod(methodToBeRemoved);

				Dictionary<MethodReferenceAndDeclaringTypeKey, bool> dict;
				if (!methodNameInfos.TryGetValue(method, out dict))
					methodNameInfos[method] = dict = new Dictionary<MethodReferenceAndDeclaringTypeKey, bool>();
				dict[new MethodReferenceAndDeclaringTypeKey(methodToBeRemoved)] = true;
			}

			public void add(MethodDefinition method, MethodReference methodToBeRemoved) {
				if (method == null || methodToBeRemoved == null)
					return;
				checkMethod(methodToBeRemoved);

				Dictionary<MethodReferenceAndDeclaringTypeKey, bool> dict;
				var methodKey = new MethodReferenceAndDeclaringTypeKey(method);
				if (!methodRefInfos.TryGetValue(methodKey, out dict))
					methodRefInfos[methodKey] = dict = new Dictionary<MethodReferenceAndDeclaringTypeKey, bool>();
				dict[new MethodReferenceAndDeclaringTypeKey(methodToBeRemoved)] = true;
			}

			public void removeAll(Blocks blocks) {
				if (blocks.Method.Name != ".cctor")
					return;

				var allBlocks = blocks.MethodBlocks.getAllBlocks();

				removeAll(allBlocks, blocks, blocks.Method.Name);
				removeAll(allBlocks, blocks, blocks.Method);
			}

			void removeAll(IList<Block> allBlocks, Blocks blocks, string method) {
				Dictionary<MethodReferenceAndDeclaringTypeKey, bool> info;
				if (!methodNameInfos.TryGetValue(method, out info))
					return;

				removeCalls(allBlocks, blocks, info);
			}

			void removeAll(IList<Block> allBlocks, Blocks blocks, MethodDefinition method) {
				Dictionary<MethodReferenceAndDeclaringTypeKey, bool> info;
				if (!methodRefInfos.TryGetValue(new MethodReferenceAndDeclaringTypeKey(method), out info))
					return;

				removeCalls(allBlocks, blocks, info);
			}

			void removeCalls(IList<Block> allBlocks, Blocks blocks, Dictionary<MethodReferenceAndDeclaringTypeKey, bool> info) {
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

						var key = new MethodReferenceAndDeclaringTypeKey(destMethod);
						if (info.ContainsKey(key)) {
							Log.v("Removed call to {0}", destMethod);
							instrsToDelete.Add(i);
						}
					}
					block.remove(instrsToDelete);
				}
			}
		}

		public void addCctorInitCallToBeRemoved(MethodReference methodToBeRemoved) {
			methodCallRemover.add(".cctor", methodToBeRemoved);
		}

		public void addModuleCctorInitCallToBeRemoved(MethodReference methodToBeRemoved) {
			methodCallRemover.add(DotNetUtils.getMethod(DotNetUtils.getModuleType(module), ".cctor"), methodToBeRemoved);
		}

		public void addCallToBeRemoved(MethodDefinition method, MethodReference methodToBeRemoved) {
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
			methodsToRemove.Add(new RemoveInfo<MethodDefinition>(method, reason));
		}

		protected void addFieldsToBeRemoved(IEnumerable<FieldDefinition> fields, string reason) {
			foreach (var field in fields)
				addFieldToBeRemoved(field, reason);
		}

		protected void addFieldToBeRemoved(FieldDefinition field, string reason) {
			fieldsToRemove.Add(new RemoveInfo<FieldDefinition>(field, reason));
		}

		protected void addAttributeToBeRemoved(TypeDefinition attr, string reason) {
			addTypeToBeRemoved(attr, reason);
			attrsToRemove.Add(new RemoveInfo<TypeDefinition>(attr, reason));
		}

		protected void addTypesToBeRemoved(IEnumerable<TypeDefinition> types, string reason) {
			foreach (var type in types)
				addTypeToBeRemoved(type, reason);
		}

		protected void addTypeToBeRemoved(TypeDefinition type, string reason) {
			typesToRemove.Add(new RemoveInfo<TypeDefinition>(type, reason));
		}

		protected void addResourceToBeRemoved(Resource resource, string reason) {
			resourcesToRemove.Add(new RemoveInfo<Resource>(resource, reason));
		}

		protected void addModuleReferenceToBeRemoved(ModuleReference modref, string reason) {
			modrefsToRemove.Add(new RemoveInfo<ModuleReference>(modref, reason));
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
				if (type.Methods.Remove(cctor))
					Log.v("{0:X8}, type: {1} ({2:X8})", cctor.MetadataToken.ToUInt32(), type, type.MetadataToken.ToUInt32());
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
				if (type.Methods.Remove(method))
					Log.v("Removed method {0} ({1:X8}) (Type: {2}) (reason: {3})", method, method.MetadataToken.ToUInt32(), type, info.reason);
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
				if (type.Fields.Remove(field))
					Log.v("Removed field {0} ({1:X8}) (Type: {2}) (reason: {3})", field, field.MetadataToken.ToUInt32(), type, info.reason);
			}
			Log.deIndent();
		}

		void deleteTypes() {
			var types = module.Types;
			if (types == null || typesToRemove.Count == 0)
				return;

			Log.v("Removing types");
			Log.indent();
			foreach (var info in typesToRemove) {
				var typeDef = info.obj;
				if (typeDef == null)
					continue;
				if (types.Remove(typeDef))
					Log.v("Removed type {0} ({1:X8}) (reason: {2})", typeDef, typeDef.MetadataToken.ToUInt32(), info.reason);
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
						Log.v("Removed custom attribute {0} ({1:X8}) (reason: {2})", typeDef, typeDef.MetadataToken.ToUInt32(), info.reason);
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
					Log.v("Removed attribute {0}", attr.FullName);
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

		protected void removeProxyDelegates(ProxyDelegateFinderBase proxyDelegateFinder) {
			addTypesToBeRemoved(proxyDelegateFinder.DelegateTypes, "Proxy delegate type");
			if (proxyDelegateFinder.RemovedDelegateCreatorCalls > 0)
				addTypeToBeRemoved(proxyDelegateFinder.DelegateCreatorMethod.DeclaringType, "Proxy delegate creator type");
		}

		protected TypeDefinition getModuleType() {
			return DotNetUtils.getModuleType(module);
		}

		protected TypeDefinition getType(TypeReference typeReference) {
			return DotNetUtils.getType(module, typeReference);
		}

		protected Resource getResource(IEnumerable<string> strings) {
			return DotNetUtils.getResource(module, strings);
		}

		protected CustomAttribute getAssemblyAttribute(TypeReference attr) {
			var list = new List<CustomAttribute>(DotNetUtils.findAttributes(module.Assembly, attr));
			return list.Count == 0 ? null : list[0];
		}
	}
}
