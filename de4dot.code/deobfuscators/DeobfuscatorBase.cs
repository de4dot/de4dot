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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using dnlib.PE;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators {
	abstract class DeobfuscatorBase : IDeobfuscator, IModuleWriterListener {
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
		protected ModuleDefMD module;
		protected StaticStringInliner staticStringInliner = new StaticStringInliner();
		IList<RemoveInfo<TypeDef>> typesToRemove = new List<RemoveInfo<TypeDef>>();
		IList<RemoveInfo<MethodDef>> methodsToRemove = new List<RemoveInfo<MethodDef>>();
		IList<RemoveInfo<FieldDef>> fieldsToRemove = new List<RemoveInfo<FieldDef>>();
		IList<RemoveInfo<TypeDef>> attrsToRemove = new List<RemoveInfo<TypeDef>>();
		IList<RemoveInfo<Resource>> resourcesToRemove = new List<RemoveInfo<Resource>>();
		List<string> namesToPossiblyRemove = new List<string>();
		MethodCallRemover methodCallRemover = new MethodCallRemover();
		byte[] moduleBytes;
		protected InitializedDataCreator initializedDataCreator;
		bool keepTypes;
		MetaDataFlags? mdFlags;

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

		public virtual MetaDataFlags MetaDataFlags {
			get { return mdFlags ?? Operations.MetaDataFlags; }
		}

		public abstract string Type { get; }
		public abstract string TypeLong { get; }
		public abstract string Name { get; }

		protected virtual bool CanInlineMethods {
			get { return false; }
		}

		protected bool KeepTypes {
			get { return keepTypes; }
			set { keepTypes = value; }
		}

		protected bool CanRemoveTypes {
			get { return !Operations.KeepObfuscatorTypes && !KeepTypes; }
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

		public virtual byte[] unpackNativeFile(IPEImage peImage) {
			return null;
		}

		public virtual void init(ModuleDefMD module) {
			setModule(module);
		}

		protected void setModule(ModuleDefMD module) {
			this.module = module;
			initializedDataCreator = new InitializedDataCreator(module);
		}

		protected void preserveTokensAndTypes() {
			keepTypes = true;
			mdFlags = Operations.MetaDataFlags;
			mdFlags |= MetaDataFlags.PreserveRids |
						MetaDataFlags.PreserveUSOffsets |
						MetaDataFlags.PreserveBlobOffsets |
						MetaDataFlags.PreserveExtraSignatureData;
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

		public virtual IDeobfuscator moduleReloaded(ModuleDefMD module) {
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
			if (CanRemoveTypes) {
				removeTypesWithInvalidBaseTypes();

				deleteEmptyCctors();
				deleteMethods();
				deleteFields();
				deleteCustomAttributes();
				deleteOtherAttributes();
				deleteTypes();
				deleteDllResources();
			}

			restoreBaseType();
			fixMDHeaderVersion();
		}

		static bool isTypeWithInvalidBaseType(TypeDef moduleType, TypeDef type) {
			return type.BaseType == null && !type.IsInterface && type != moduleType;
		}

		void restoreBaseType() {
			var moduleType = DotNetUtils.getModuleType(module);
			foreach (var type in module.GetTypes()) {
				if (!isTypeWithInvalidBaseType(moduleType, type))
					continue;
				var corSig = module.CorLibTypes.GetCorLibTypeSig(type);
				if (corSig != null && corSig.ElementType == ElementType.Object)
					continue;
				Logger.v("Adding System.Object as base type: {0} ({1:X8})",
							Utils.removeNewlines(type),
							type.MDToken.ToInt32());
				type.BaseType = module.CorLibTypes.Object.TypeDefOrRef;
			}
		}

		void fixMDHeaderVersion() {
			// Version 1.1 supports generics but it's a little different. Most tools
			// will have a problem reading the MD tables, so switch to the standard v2.0.
			if (module.TablesHeaderVersion == 0x0101)
				module.TablesHeaderVersion = 0x0200;
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
			Dictionary<string, MethodDefAndDeclaringTypeDict<bool>> methodNameInfos = new Dictionary<string, MethodDefAndDeclaringTypeDict<bool>>();
			MethodDefAndDeclaringTypeDict<MethodDefAndDeclaringTypeDict<bool>> methodRefInfos = new MethodDefAndDeclaringTypeDict<MethodDefAndDeclaringTypeDict<bool>>();

			void checkMethod(IMethod methodToBeRemoved) {
				var sig = methodToBeRemoved.MethodSig;
				if (sig.Params.Count != 0)
					throw new ApplicationException(string.Format("Method takes params: {0}", methodToBeRemoved));
				if (sig.RetType.ElementType != ElementType.Void)
					throw new ApplicationException(string.Format("Method has a return value: {0}", methodToBeRemoved));
			}

			public void add(string method, MethodDef methodToBeRemoved) {
				if (methodToBeRemoved == null)
					return;
				checkMethod(methodToBeRemoved);

				MethodDefAndDeclaringTypeDict<bool> dict;
				if (!methodNameInfos.TryGetValue(method, out dict))
					methodNameInfos[method] = dict = new MethodDefAndDeclaringTypeDict<bool>();
				dict.add(methodToBeRemoved, true);
			}

			public void add(MethodDef method, MethodDef methodToBeRemoved) {
				if (method == null || methodToBeRemoved == null)
					return;
				checkMethod(methodToBeRemoved);

				var dict = methodRefInfos.find(method);
				if (dict == null)
					methodRefInfos.add(method, dict = new MethodDefAndDeclaringTypeDict<bool>());
				dict.add(methodToBeRemoved, true);
			}

			public void removeAll(Blocks blocks) {
				var allBlocks = blocks.MethodBlocks.getAllBlocks();

				removeAll(allBlocks, blocks, blocks.Method.Name.String);
				removeAll(allBlocks, blocks, blocks.Method);
			}

			void removeAll(IList<Block> allBlocks, Blocks blocks, string method) {
				MethodDefAndDeclaringTypeDict<bool> info;
				if (!methodNameInfos.TryGetValue(method, out info))
					return;

				removeCalls(allBlocks, blocks, info);
			}

			void removeAll(IList<Block> allBlocks, Blocks blocks, MethodDef method) {
				var info = methodRefInfos.find(method);
				if (info == null)
					return;

				removeCalls(allBlocks, blocks, info);
			}

			void removeCalls(IList<Block> allBlocks, Blocks blocks, MethodDefAndDeclaringTypeDict<bool> info) {
				var instrsToDelete = new List<int>();
				foreach (var block in allBlocks) {
					instrsToDelete.Clear();
					for (int i = 0; i < block.Instructions.Count; i++) {
						var instr = block.Instructions[i];
						if (instr.OpCode != OpCodes.Call)
							continue;
						var destMethod = instr.Operand as IMethod;
						if (destMethod == null)
							continue;

						if (info.find(destMethod)) {
							Logger.v("Removed call to {0}", Utils.removeNewlines(destMethod));
							instrsToDelete.Add(i);
						}
					}
					block.remove(instrsToDelete);
				}
			}
		}

		public void addCctorInitCallToBeRemoved(MethodDef methodToBeRemoved) {
			methodCallRemover.add(".cctor", methodToBeRemoved);
		}

		public void addModuleCctorInitCallToBeRemoved(MethodDef methodToBeRemoved) {
			methodCallRemover.add(DotNetUtils.getModuleTypeCctor(module), methodToBeRemoved);
		}

		public void addCtorInitCallToBeRemoved(MethodDef methodToBeRemoved) {
			methodCallRemover.add(".ctor", methodToBeRemoved);
		}

		public void addCallToBeRemoved(MethodDef method, MethodDef methodToBeRemoved) {
			methodCallRemover.add(method, methodToBeRemoved);
		}

		void removeMethodCalls(Blocks blocks) {
			methodCallRemover.removeAll(blocks);
		}

		protected void addMethodsToBeRemoved(IEnumerable<MethodDef> methods, string reason) {
			foreach (var method in methods)
				addMethodToBeRemoved(method, reason);
		}

		protected void addMethodToBeRemoved(MethodDef method, string reason) {
			if (method != null)
				methodsToRemove.Add(new RemoveInfo<MethodDef>(method, reason));
		}

		protected void addFieldsToBeRemoved(IEnumerable<FieldDef> fields, string reason) {
			foreach (var field in fields)
				addFieldToBeRemoved(field, reason);
		}

		protected void addFieldToBeRemoved(FieldDef field, string reason) {
			if (field != null)
				fieldsToRemove.Add(new RemoveInfo<FieldDef>(field, reason));
		}

		protected void addAttributesToBeRemoved(IEnumerable<TypeDef> attrs, string reason) {
			foreach (var attr in attrs)
				addAttributeToBeRemoved(attr, reason);
		}

		protected void addAttributeToBeRemoved(TypeDef attr, string reason) {
			if (attr == null)
				return;
			addTypeToBeRemoved(attr, reason);
			attrsToRemove.Add(new RemoveInfo<TypeDef>(attr, reason));
		}

		protected void addTypesToBeRemoved(IEnumerable<TypeDef> types, string reason) {
			foreach (var type in types)
				addTypeToBeRemoved(type, reason);
		}

		protected void addTypeToBeRemoved(TypeDef type, string reason) {
			if (type != null)
				typesToRemove.Add(new RemoveInfo<TypeDef>(type, reason));
		}

		protected void addResourceToBeRemoved(Resource resource, string reason) {
			if (resource != null)
				resourcesToRemove.Add(new RemoveInfo<Resource>(resource, reason));
		}

		void deleteEmptyCctors() {
			var emptyCctorsToRemove = new List<MethodDef>();
			foreach (var type in module.GetTypes()) {
				var cctor = type.FindStaticConstructor();
				if (cctor != null && DotNetUtils.isEmpty(cctor))
					emptyCctorsToRemove.Add(cctor);
			}

			if (emptyCctorsToRemove.Count == 0)
				return;

			Logger.v("Removing empty .cctor methods");
			Logger.Instance.indent();
			foreach (var cctor in emptyCctorsToRemove) {
				var type = cctor.DeclaringType;
				if (type == null)
					continue;
				if (type.Methods.Remove(cctor))
					Logger.v("{0:X8}, type: {1} ({2:X8})",
								cctor.MDToken.ToUInt32(),
								Utils.removeNewlines(type),
								type.MDToken.ToUInt32());
			}
			Logger.Instance.deIndent();
		}

		void deleteMethods() {
			if (methodsToRemove.Count == 0)
				return;

			Logger.v("Removing methods");
			Logger.Instance.indent();
			foreach (var info in methodsToRemove) {
				var method = info.obj;
				if (method == null)
					continue;
				var type = method.DeclaringType;
				if (type == null)
					continue;
				if (type.Methods.Remove(method))
					Logger.v("Removed method {0} ({1:X8}) (Type: {2}) (reason: {3})",
								Utils.removeNewlines(method),
								method.MDToken.ToUInt32(),
								Utils.removeNewlines(type),
								info.reason);
			}
			Logger.Instance.deIndent();
		}

		void deleteFields() {
			if (fieldsToRemove.Count == 0)
				return;

			Logger.v("Removing fields");
			Logger.Instance.indent();
			foreach (var info in fieldsToRemove) {
				var field = info.obj;
				if (field == null)
					continue;
				var type = field.DeclaringType;
				if (type == null)
					continue;
				if (type.Fields.Remove(field))
					Logger.v("Removed field {0} ({1:X8}) (Type: {2}) (reason: {3})",
								Utils.removeNewlines(field),
								field.MDToken.ToUInt32(),
								Utils.removeNewlines(type),
								info.reason);
			}
			Logger.Instance.deIndent();
		}

		void deleteTypes() {
			var types = module.Types;
			if (types == null || typesToRemove.Count == 0)
				return;

			Logger.v("Removing types");
			Logger.Instance.indent();
			var moduleType = DotNetUtils.getModuleType(module);
			foreach (var info in typesToRemove) {
				var typeDef = info.obj;
				if (typeDef == null || typeDef == moduleType)
					continue;
				bool removed;
				if (typeDef.DeclaringType != null)
					removed = typeDef.DeclaringType.NestedTypes.Remove(typeDef);
				else
					removed = types.Remove(typeDef);
				if (removed)
					Logger.v("Removed type {0} ({1:X8}) (reason: {2})",
								Utils.removeNewlines(typeDef),
								typeDef.MDToken.ToUInt32(),
								info.reason);
			}
			Logger.Instance.deIndent();
		}

		void deleteCustomAttributes() {
			if (attrsToRemove.Count == 0)
				return;

			Logger.v("Removing custom attributes");
			Logger.Instance.indent();
			deleteCustomAttributes(module.CustomAttributes);
			if (module.Assembly != null)
				deleteCustomAttributes(module.Assembly.CustomAttributes);
			Logger.Instance.deIndent();
		}

		void deleteCustomAttributes(IList<CustomAttribute> customAttrs) {
			if (customAttrs == null)
				return;
			foreach (var info in attrsToRemove) {
				var typeDef = info.obj;
				if (typeDef == null)
					continue;
				for (int i = 0; i < customAttrs.Count; i++) {
					if (new SigComparer().Equals(typeDef, customAttrs[i].AttributeType)) {
						customAttrs.RemoveAt(i);
						i--;
						Logger.v("Removed custom attribute {0} ({1:X8}) (reason: {2})",
									Utils.removeNewlines(typeDef),
									typeDef.MDToken.ToUInt32(),
									info.reason);
						break;
					}
				}
			}
		}

		void deleteOtherAttributes() {
			Logger.v("Removing other attributes");
			Logger.Instance.indent();
			deleteOtherAttributes(module.CustomAttributes);
			if (module.Assembly != null)
				deleteOtherAttributes(module.Assembly.CustomAttributes);
			Logger.Instance.deIndent();
		}

		void deleteOtherAttributes(IList<CustomAttribute> customAttributes) {
			for (int i = customAttributes.Count - 1; i >= 0; i--) {
				var attr = customAttributes[i].TypeFullName;
				if (attr == "System.Runtime.CompilerServices.SuppressIldasmAttribute") {
					Logger.v("Removed attribute {0}", Utils.removeNewlines(attr));
					customAttributes.RemoveAt(i);
				}
			}
		}

		void deleteDllResources() {
			if (!module.HasResources || resourcesToRemove.Count == 0)
				return;

			Logger.v("Removing resources");
			Logger.Instance.indent();
			foreach (var info in resourcesToRemove) {
				var resource = info.obj;
				if (resource == null)
					continue;
				if (module.Resources.Remove(resource))
					Logger.v("Removed resource {0} (reason: {1})", Utils.toCsharpString(resource.Name), info.reason);
			}
			Logger.Instance.deIndent();
		}

		protected void setInitLocals() {
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (isFatHeader(method))
						method.Body.InitLocals = true;
				}
			}
		}

		static bool isFatHeader(MethodDef method) {
			if (method == null || method.Body == null)
				return false;
			var body = method.Body;
			if (body.InitLocals || body.MaxStack > 8)
				return true;
			if (body.Variables.Count > 0)
				return true;
			if (body.ExceptionHandlers.Count > 0)
				return true;
			if (getCodeSize(method) > 63)
				return true;

			return false;
		}

		static int getCodeSize(MethodDef method) {
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

		protected void findPossibleNamesToRemove(MethodDef method) {
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

		protected bool removeProxyDelegates(ProxyCallFixerBase proxyCallFixer) {
			return removeProxyDelegates(proxyCallFixer, true);
		}

		protected bool removeProxyDelegates(ProxyCallFixerBase proxyCallFixer, bool removeCreators) {
			if (proxyCallFixer.Errors != 0) {
				Logger.v("Not removing proxy delegates and creator type since errors were detected.");
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

		protected CustomAttribute getAssemblyAttribute(IType attr) {
			if (module.Assembly == null)
				return null;
			return module.Assembly.CustomAttributes.Find(attr);
		}

		protected CustomAttribute getModuleAttribute(IType attr) {
			return module.CustomAttributes.Find(attr);
		}

		protected bool hasMetadataStream(string name) {
			foreach (var stream in module.MetaData.AllStreams) {
				if (stream.Name == name)
					return true;
			}
			return false;
		}

		List<T> getObjectsToRemove<T>(IList<RemoveInfo<T>> removeThese) where T : class, ICodedToken {
			var list = new List<T>(removeThese.Count);
			foreach (var info in removeThese) {
				if (info.obj != null)
					list.Add(info.obj);
			}
			return list;
		}

		protected List<TypeDef> getTypesToRemove() {
			return getObjectsToRemove(typesToRemove);
		}

		protected List<MethodDef> getMethodsToRemove() {
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

		public virtual bool isValidMethodReturnArgName(string name) {
			return string.IsNullOrEmpty(name) || checkValidName(name);
		}

		public virtual bool isValidResourceKeyName(string name) {
			return name != null && checkValidName(name);
		}

		public virtual void OnWriterEvent(ModuleWriterBase writer, ModuleWriterEvent evt) {
		}

		protected void findAndRemoveInlinedMethods() {
			removeInlinedMethods(InlinedMethodsFinder.find(module));
		}

		protected void removeInlinedMethods(List<MethodDef> inlinedMethods) {
			addMethodsToBeRemoved(new UnusedMethodsFinder(module, inlinedMethods, getRemovedMethods()).find(), "Inlined method");
		}

		protected MethodCollection getRemovedMethods() {
			var removedMethods = new MethodCollection();
			removedMethods.add(getMethodsToRemove());
			removedMethods.addAndNested(getTypesToRemove());
			return removedMethods;
		}

		protected bool isTypeCalled(TypeDef decrypterType) {
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
							var calledMethod = instr.Operand as IMethod;
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

		protected bool hasNativeMethods() {
			if (module.VTableFixups != null)
				return true;
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					var mb = method.MethodBody;
					if (mb == null)
						continue;
					if (mb is CilBody)
						continue;
					return true;
				}
			}
			return false;
		}

		protected static int toInt32(bool b) {
			return b ? 1 : 0;
		}

		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing) {
		}
	}
}
