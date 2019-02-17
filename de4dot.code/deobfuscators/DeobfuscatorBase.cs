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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using dnlib.PE;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators {
	public abstract class DeobfuscatorBase : IDeobfuscator, IModuleWriterListener {
		public const string DEFAULT_VALID_NAME_REGEX = @"^[a-zA-Z_<{$][a-zA-Z_0-9<>{}$.`-]*$";
		public const string DEFAULT_ASIAN_VALID_NAME_REGEX = @"^[\u2E80-\u9FFFa-zA-Z_<{$][\u2E80-\u9FFFa-zA-Z_0-9<>{}$.`-]*$";

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
		MetadataFlags? mdFlags;
		Dictionary<object, bool> objectsThatMustBeKept = new Dictionary<object, bool>();

		protected byte[] ModuleBytes {
			get => moduleBytes;
			set => moduleBytes = value;
		}

		public class OptionsBase : IDeobfuscatorOptions {
			public bool RenameResourcesInCode { get; set; }
			public NameRegexes ValidNameRegex { get; set; }
			public bool DecryptStrings { get; set; }
			public OptionsBase() => RenameResourcesInCode = true;
		}

		public IDeobfuscatorOptions TheOptions => optionsBase;
		public IOperations Operations { get; set; }
		public IDeobfuscatedFile DeobfuscatedFile { get; set; }
		public virtual StringFeatures StringFeatures { get; set; }
		public virtual RenamingOptions RenamingOptions { get; set; }
		public DecrypterType DefaultDecrypterType { get; set; }
		public virtual MetadataFlags MetadataFlags => mdFlags ?? Operations.MetadataFlags;
		public abstract string Type { get; }
		public abstract string TypeLong { get; }
		public abstract string Name { get; }
		protected virtual bool CanInlineMethods => false;

		protected bool KeepTypes {
			get => keepTypes;
			set => keepTypes = value;
		}

		protected bool CanRemoveTypes => !Operations.KeepObfuscatorTypes && !KeepTypes;
		protected bool CanRemoveStringDecrypterType => Operations.DecryptStrings != OpDecryptString.None && staticStringInliner.InlinedAllCalls;

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

		public virtual byte[] UnpackNativeFile(IPEImage peImage) => null;
		public virtual void Initialize(ModuleDefMD module) => SetModule(module);

		protected void SetModule(ModuleDefMD module) {
			this.module = module;
			initializedDataCreator = new InitializedDataCreator(module);
		}

		protected void PreserveTokensAndTypes() {
			keepTypes = true;
			mdFlags = Operations.MetadataFlags;
			mdFlags |= MetadataFlags.PreserveRids |
						MetadataFlags.PreserveUSOffsets |
						MetadataFlags.PreserveBlobOffsets |
						MetadataFlags.PreserveExtraSignatureData;
		}

		protected virtual bool CheckValidName(string name) => optionsBase.ValidNameRegex.IsMatch(name);

		public virtual int Detect() {
			ScanForObfuscator();
			return DetectInternal();
		}

		protected abstract void ScanForObfuscator();
		protected abstract int DetectInternal();

		public virtual bool GetDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) => false;

		public virtual IDeobfuscator ModuleReloaded(ModuleDefMD module) =>
			throw new ApplicationException("moduleReloaded() must be overridden by the deobfuscator");

		public virtual void DeobfuscateBegin() => ModuleBytes = null;
		public virtual void DeobfuscateMethodBegin(Blocks blocks) { }
		public virtual void DeobfuscateMethodEnd(Blocks blocks) => RemoveMethodCalls(blocks);
		public virtual void DeobfuscateStrings(Blocks blocks) => staticStringInliner.Decrypt(blocks);
		public virtual bool DeobfuscateOther(Blocks blocks) => false;

		public virtual void DeobfuscateEnd() {
			// Make sure the TypeDefCache isn't enabled while we modify types or remove stuff
			bool cacheState = module.EnableTypeDefFindCache;
			module.EnableTypeDefFindCache = false;

			if (CanRemoveTypes) {
				InitializeObjectsToKeepFromVTableFixups();

				RemoveTypesWithInvalidBaseTypes();

				DeleteEmptyCctors();
				DeleteMethods();
				DeleteFields();
				DeleteCustomAttributes();
				DeleteOtherAttributes();
				DeleteTypes();
				DeleteDllResources();
			}

			RestoreBaseType();
			FixMDHeaderVersion();

			module.Mvid = Guid.NewGuid();
			module.EnableTypeDefFindCache = cacheState;
		}

		void InitializeObjectsToKeepFromVTableFixups() {
			var fixups = module.VTableFixups;
			if (fixups == null || fixups.VTables.Count == 0)
				return;

			foreach (var vtable in fixups) {
				if (vtable == null)
					continue;
				foreach (var method in vtable) {
					if (method == null)
						continue;
					objectsThatMustBeKept[method] = true;
				}
			}
		}

		bool MustKeepObject(object o) => o != null && objectsThatMustBeKept.ContainsKey(o);

		static bool IsTypeWithInvalidBaseType(TypeDef moduleType, TypeDef type) =>
			type.BaseType == null && !type.IsInterface && type != moduleType;

		void RestoreBaseType() {
			var moduleType = DotNetUtils.GetModuleType(module);
			foreach (var type in module.GetTypes()) {
				if (!IsTypeWithInvalidBaseType(moduleType, type))
					continue;
				var corSig = module.CorLibTypes.GetCorLibTypeSig(type);
				if (corSig != null && corSig.ElementType == ElementType.Object)
					continue;
				Logger.v("Adding System.Object as base type: {0} ({1:X8})",
							Utils.RemoveNewlines(type),
							type.MDToken.ToInt32());
				type.BaseType = module.CorLibTypes.Object.TypeDefOrRef;
			}
		}

		void FixMDHeaderVersion() {
			// Version 1.1 supports generics but it's a little different. Most tools
			// will have a problem reading the MD tables, so switch to the standard v2.0.
			if (module.TablesHeaderVersion == 0x0101)
				module.TablesHeaderVersion = 0x0200;
		}

		void RemoveTypesWithInvalidBaseTypes() {
			var moduleType = DotNetUtils.GetModuleType(module);
			foreach (var type in module.GetTypes()) {
				if (!IsTypeWithInvalidBaseType(moduleType, type) || MustKeepObject(type))
					continue;
				AddTypeToBeRemoved(type, "Invalid type with no base type (anti-reflection)");
			}
		}

		protected void FixEnumTypes() {
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

		protected void FixInterfaces() {
			foreach (var type in module.GetTypes()) {
				if (!type.IsInterface)
					continue;
				type.IsSealed = false;
			}
		}

		public abstract IEnumerable<int> GetStringDecrypterMethods();

		class MethodCallRemover {
			Dictionary<string, MethodDefAndDeclaringTypeDict<bool>> methodNameInfos = new Dictionary<string, MethodDefAndDeclaringTypeDict<bool>>();
			MethodDefAndDeclaringTypeDict<MethodDefAndDeclaringTypeDict<bool>> methodRefInfos = new MethodDefAndDeclaringTypeDict<MethodDefAndDeclaringTypeDict<bool>>();

			void CheckMethod(IMethod methodToBeRemoved) {
				var sig = methodToBeRemoved.MethodSig;
				if (sig.Params.Count != 0)
					throw new ApplicationException($"Method takes params: {methodToBeRemoved}");
				if (sig.RetType.ElementType != ElementType.Void)
					throw new ApplicationException($"Method has a return value: {methodToBeRemoved}");
			}

			public void Add(string method, MethodDef methodToBeRemoved) {
				if (methodToBeRemoved == null)
					return;
				CheckMethod(methodToBeRemoved);

				if (!methodNameInfos.TryGetValue(method, out var dict))
					methodNameInfos[method] = dict = new MethodDefAndDeclaringTypeDict<bool>();
				dict.Add(methodToBeRemoved, true);
			}

			public void Add(MethodDef method, MethodDef methodToBeRemoved) {
				if (method == null || methodToBeRemoved == null)
					return;
				CheckMethod(methodToBeRemoved);

				var dict = methodRefInfos.Find(method);
				if (dict == null)
					methodRefInfos.Add(method, dict = new MethodDefAndDeclaringTypeDict<bool>());
				dict.Add(methodToBeRemoved, true);
			}

			public void RemoveAll(Blocks blocks) {
				var allBlocks = blocks.MethodBlocks.GetAllBlocks();

				RemoveAll(allBlocks, blocks, blocks.Method.Name.String);
				RemoveAll(allBlocks, blocks, blocks.Method);
			}

			void RemoveAll(IList<Block> allBlocks, Blocks blocks, string method) {
				if (!methodNameInfos.TryGetValue(method, out var info))
					return;

				RemoveCalls(allBlocks, blocks, info);
			}

			void RemoveAll(IList<Block> allBlocks, Blocks blocks, MethodDef method) {
				var info = methodRefInfos.Find(method);
				if (info == null)
					return;

				RemoveCalls(allBlocks, blocks, info);
			}

			void RemoveCalls(IList<Block> allBlocks, Blocks blocks, MethodDefAndDeclaringTypeDict<bool> info) {
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

						if (info.Find(destMethod)) {
							Logger.v("Removed call to {0}", Utils.RemoveNewlines(destMethod));
							instrsToDelete.Add(i);
						}
					}
					block.Remove(instrsToDelete);
				}
			}
		}

		public void AddCctorInitCallToBeRemoved(MethodDef methodToBeRemoved) =>
			methodCallRemover.Add(".cctor", methodToBeRemoved);

		public void AddModuleCctorInitCallToBeRemoved(MethodDef methodToBeRemoved) =>
			methodCallRemover.Add(DotNetUtils.GetModuleTypeCctor(module), methodToBeRemoved);

		public void AddCtorInitCallToBeRemoved(MethodDef methodToBeRemoved) =>
			methodCallRemover.Add(".ctor", methodToBeRemoved);

		public void AddCallToBeRemoved(MethodDef method, MethodDef methodToBeRemoved) =>
			methodCallRemover.Add(method, methodToBeRemoved);

		void RemoveMethodCalls(Blocks blocks) =>
			methodCallRemover.RemoveAll(blocks);

		protected void AddMethodsToBeRemoved(IEnumerable<MethodDef> methods, string reason) {
			foreach (var method in methods)
				AddMethodToBeRemoved(method, reason);
		}

		protected void AddMethodToBeRemoved(MethodDef method, string reason) {
			if (method != null)
				methodsToRemove.Add(new RemoveInfo<MethodDef>(method, reason));
		}

		protected void AddFieldsToBeRemoved(IEnumerable<FieldDef> fields, string reason) {
			foreach (var field in fields)
				AddFieldToBeRemoved(field, reason);
		}

		protected void AddFieldToBeRemoved(FieldDef field, string reason) {
			if (field != null)
				fieldsToRemove.Add(new RemoveInfo<FieldDef>(field, reason));
		}

		protected void AddAttributesToBeRemoved(IEnumerable<TypeDef> attrs, string reason) {
			foreach (var attr in attrs)
				AddAttributeToBeRemoved(attr, reason);
		}

		protected void AddAttributeToBeRemoved(TypeDef attr, string reason) {
			if (attr == null)
				return;
			AddTypeToBeRemoved(attr, reason);
			attrsToRemove.Add(new RemoveInfo<TypeDef>(attr, reason));
		}

		protected void AddTypesToBeRemoved(IEnumerable<TypeDef> types, string reason) {
			foreach (var type in types)
				AddTypeToBeRemoved(type, reason);
		}

		protected void AddTypeToBeRemoved(TypeDef type, string reason) {
			if (type != null)
				typesToRemove.Add(new RemoveInfo<TypeDef>(type, reason));
		}

		protected void AddResourceToBeRemoved(Resource resource, string reason) {
			if (resource != null)
				resourcesToRemove.Add(new RemoveInfo<Resource>(resource, reason));
		}

		void DeleteEmptyCctors() {
			var emptyCctorsToRemove = new List<MethodDef>();
			foreach (var type in module.GetTypes()) {
				var cctor = type.FindStaticConstructor();
				if (cctor != null && DotNetUtils.IsEmpty(cctor) && !MustKeepObject(cctor))
					emptyCctorsToRemove.Add(cctor);
			}

			if (emptyCctorsToRemove.Count == 0)
				return;

			Logger.v("Removing empty .cctor methods");
			Logger.Instance.Indent();
			foreach (var cctor in emptyCctorsToRemove) {
				var type = cctor.DeclaringType;
				if (type == null)
					continue;
				if (type.Methods.Remove(cctor))
					Logger.v("{0:X8}, type: {1} ({2:X8})",
								cctor.MDToken.ToUInt32(),
								Utils.RemoveNewlines(type),
								type.MDToken.ToUInt32());
			}
			Logger.Instance.DeIndent();
		}

		void DeleteMethods() {
			if (methodsToRemove.Count == 0)
				return;

			Logger.v("Removing methods");
			Logger.Instance.Indent();
			foreach (var info in methodsToRemove) {
				var method = info.obj;
				if (method == null || MustKeepObject(method))
					continue;
				var type = method.DeclaringType;
				if (type == null)
					continue;
				if (type.Methods.Remove(method))
					Logger.v("Removed method {0} ({1:X8}) (Type: {2}) (reason: {3})",
								Utils.RemoveNewlines(method),
								method.MDToken.ToUInt32(),
								Utils.RemoveNewlines(type),
								info.reason);
			}
			Logger.Instance.DeIndent();
		}

		void DeleteFields() {
			if (fieldsToRemove.Count == 0)
				return;

			Logger.v("Removing fields");
			Logger.Instance.Indent();
			foreach (var info in fieldsToRemove) {
				var field = info.obj;
				if (field == null || MustKeepObject(field))
					continue;
				var type = field.DeclaringType;
				if (type == null)
					continue;
				if (type.Fields.Remove(field))
					Logger.v("Removed field {0} ({1:X8}) (Type: {2}) (reason: {3})",
								Utils.RemoveNewlines(field),
								field.MDToken.ToUInt32(),
								Utils.RemoveNewlines(type),
								info.reason);
			}
			Logger.Instance.DeIndent();
		}

		void DeleteTypes() {
			var types = module.Types;
			if (types == null || typesToRemove.Count == 0)
				return;

			Logger.v("Removing types");
			Logger.Instance.Indent();
			var moduleType = DotNetUtils.GetModuleType(module);
			foreach (var info in typesToRemove) {
				var typeDef = info.obj;
				if (typeDef == null || typeDef == moduleType || MustKeepObject(typeDef))
					continue;
				bool removed;
				if (typeDef.DeclaringType != null)
					removed = typeDef.DeclaringType.NestedTypes.Remove(typeDef);
				else
					removed = types.Remove(typeDef);
				if (removed)
					Logger.v("Removed type {0} ({1:X8}) (reason: {2})",
								Utils.RemoveNewlines(typeDef),
								typeDef.MDToken.ToUInt32(),
								info.reason);
			}
			Logger.Instance.DeIndent();
		}

		void DeleteCustomAttributes() {
			if (attrsToRemove.Count == 0)
				return;

			Logger.v("Removing custom attributes");
			Logger.Instance.Indent();
			DeleteCustomAttributes(module.CustomAttributes);
			if (module.Assembly != null)
				DeleteCustomAttributes(module.Assembly.CustomAttributes);
			Logger.Instance.DeIndent();
		}

		void DeleteCustomAttributes(IList<CustomAttribute> customAttrs) {
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
									Utils.RemoveNewlines(typeDef),
									typeDef.MDToken.ToUInt32(),
									info.reason);
						break;
					}
				}
			}
		}

		void DeleteOtherAttributes() {
			Logger.v("Removing other attributes");
			Logger.Instance.Indent();
			DeleteOtherAttributes(module.CustomAttributes);
			if (module.Assembly != null)
				DeleteOtherAttributes(module.Assembly.CustomAttributes);
			Logger.Instance.DeIndent();
		}

		void DeleteOtherAttributes(IList<CustomAttribute> customAttributes) {
			for (int i = customAttributes.Count - 1; i >= 0; i--) {
				var attr = customAttributes[i].TypeFullName;
				if (attr == "System.Runtime.CompilerServices.SuppressIldasmAttribute") {
					Logger.v("Removed attribute {0}", Utils.RemoveNewlines(attr));
					customAttributes.RemoveAt(i);
				}
			}
		}

		void DeleteDllResources() {
			if (!module.HasResources || resourcesToRemove.Count == 0)
				return;

			Logger.v("Removing resources");
			Logger.Instance.Indent();
			foreach (var info in resourcesToRemove) {
				var resource = info.obj;
				if (resource == null || MustKeepObject(resource))
					continue;
				if (module.Resources.Remove(resource))
					Logger.v("Removed resource {0} (reason: {1})", Utils.ToCsharpString(resource.Name), info.reason);
			}
			Logger.Instance.DeIndent();
		}

		protected void SetInitLocals() {
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (IsFatHeader(method))
						method.Body.InitLocals = true;
				}
			}
		}

		static bool IsFatHeader(MethodDef method) {
			if (method == null || method.Body == null)
				return false;
			var body = method.Body;
			if (body.InitLocals || body.MaxStack > 8)
				return true;
			if (body.Variables.Count > 0)
				return true;
			if (body.ExceptionHandlers.Count > 0)
				return true;
			if (GetCodeSize(method) > 63)
				return true;

			return false;
		}

		static int GetCodeSize(MethodDef method) {
			if (method == null || method.Body == null)
				return 0;
			int size = 0;
			foreach (var instr in method.Body.Instructions)
				size += instr.GetSize();
			return size;
		}

		public override string ToString() => Name;

		protected void FindPossibleNamesToRemove(MethodDef method) {
			if (method == null || !method.HasBody)
				return;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode == OpCodes.Ldstr)
					namesToPossiblyRemove.Add((string)instr.Operand);
			}
		}

		protected void AddResources(string reason) {
			if (!module.HasResources)
				return;

			foreach (var name in namesToPossiblyRemove) {
				foreach (var resource in module.Resources) {
					if (resource.Name == name) {
						AddResourceToBeRemoved(resource, reason);
						break;
					}
				}
			}
		}

		protected bool RemoveProxyDelegates(ProxyCallFixerBase proxyCallFixer) =>
			RemoveProxyDelegates(proxyCallFixer, true);

		protected bool RemoveProxyDelegates(ProxyCallFixerBase proxyCallFixer, bool removeCreators) {
			if (proxyCallFixer.Errors != 0) {
				Logger.v("Not removing proxy delegates and creator type since errors were detected.");
				return false;
			}
			AddTypesToBeRemoved(proxyCallFixer.DelegateTypes, "Proxy delegate type");
			if (removeCreators && proxyCallFixer.RemovedDelegateCreatorCalls > 0) {
				AddTypesToBeRemoved(proxyCallFixer.DelegateCreatorTypes, "Proxy delegate creator type");
				foreach (var tuple in proxyCallFixer.OtherMethods)
					AddMethodToBeRemoved(tuple.Item1, tuple.Item2);
			}
			return true;
		}

		protected Resource GetResource(IEnumerable<string> strings) => DotNetUtils.GetResource(module, strings);

		protected CustomAttribute GetAssemblyAttribute(IType attr) {
			if (module.Assembly == null)
				return null;
			return module.Assembly.CustomAttributes.Find(attr);
		}

		protected CustomAttribute GetModuleAttribute(IType attr) => module.CustomAttributes.Find(attr);

		protected bool HasMetadataStream(string name) {
			foreach (var stream in module.Metadata.AllStreams) {
				if (stream.Name == name)
					return true;
			}
			return false;
		}

		List<T> GetObjectsToRemove<T>(IList<RemoveInfo<T>> removeThese) where T : class, ICodedToken {
			var list = new List<T>(removeThese.Count);
			foreach (var info in removeThese) {
				if (info.obj != null)
					list.Add(info.obj);
			}
			return list;
		}

		protected List<TypeDef> GetTypesToRemove() => GetObjectsToRemove(typesToRemove);
		protected List<MethodDef> GetMethodsToRemove() => GetObjectsToRemove(methodsToRemove);

		public virtual bool IsValidNamespaceName(string ns) {
			if (ns == null)
				return false;
			foreach (var part in ns.Split(new char[] { '.' })) {
				if (!CheckValidName(part))
					return false;
			}
			return true;
		}

		public virtual bool IsValidTypeName(string name) => name != null && CheckValidName(name);
		public virtual bool IsValidMethodName(string name) => name != null && CheckValidName(name);
		public virtual bool IsValidPropertyName(string name) => name != null && CheckValidName(name);
		public virtual bool IsValidEventName(string name) => name != null && CheckValidName(name);
		public virtual bool IsValidFieldName(string name) => name != null && CheckValidName(name);
		public virtual bool IsValidGenericParamName(string name) => name != null && CheckValidName(name);
		public virtual bool IsValidMethodArgName(string name) => name != null && CheckValidName(name);
		public virtual bool IsValidMethodReturnArgName(string name) => string.IsNullOrEmpty(name) || CheckValidName(name);
		public virtual bool IsValidResourceKeyName(string name) => name != null && CheckValidName(name);
		public virtual void OnWriterEvent(ModuleWriterBase writer, ModuleWriterEvent evt) { }
		protected void FindAndRemoveInlinedMethods() => RemoveInlinedMethods(InlinedMethodsFinder.Find(module));
		protected void RemoveInlinedMethods(List<MethodDef> inlinedMethods) =>
			AddMethodsToBeRemoved(new UnusedMethodsFinder(module, inlinedMethods, GetRemovedMethods()).Find(), "Inlined method");

		protected MethodCollection GetRemovedMethods() {
			var removedMethods = new MethodCollection();
			removedMethods.Add(GetMethodsToRemove());
			removedMethods.AddAndNested(GetTypesToRemove());
			return removedMethods;
		}

		protected bool IsTypeCalled(TypeDef decrypterType) {
			if (decrypterType == null)
				return false;

			var decrypterMethods = new MethodCollection();
			decrypterMethods.AddAndNested(decrypterType);

			var removedMethods = GetRemovedMethods();

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (method.Body == null)
						continue;
					if (decrypterMethods.Exists(method))
						break;	// decrypter type / nested type method
					if (removedMethods.Exists(method))
						continue;

					foreach (var instr in method.Body.Instructions) {
						switch (instr.OpCode.Code) {
						case Code.Call:
						case Code.Callvirt:
						case Code.Newobj:
							var calledMethod = instr.Operand as IMethod;
							if (calledMethod == null)
								break;
							if (decrypterMethods.Exists(calledMethod))
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

		protected bool HasNativeMethods() {
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

		protected static int ToInt32(bool b) => b ? 1 : 0;

		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing) { }
	}
}
