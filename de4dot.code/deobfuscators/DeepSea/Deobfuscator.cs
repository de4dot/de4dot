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
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.DeepSea {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "DeepSea";
		public const string THE_TYPE = "ds";
		const string DEFAULT_REGEX = DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;
		BoolOption decryptResources;
		BoolOption dumpEmbeddedAssemblies;
		BoolOption restoreFields;
		BoolOption renameResourceKeys;
		BoolOption castDeobfuscation;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			inlineMethods = new BoolOption(null, MakeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, MakeArgName("remove-inlined"), "Remove inlined methods", true);
			decryptResources = new BoolOption(null, MakeArgName("rsrc"), "Decrypt resources", true);
			dumpEmbeddedAssemblies = new BoolOption(null, MakeArgName("embedded"), "Dump embedded assemblies", true);
			restoreFields = new BoolOption(null, MakeArgName("fields"), "Restore fields", true);
			renameResourceKeys = new BoolOption(null, MakeArgName("keys"), "Rename resource keys", true);
			castDeobfuscation = new BoolOption(null, MakeArgName("casts"), "Deobfuscate casts", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator CreateDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
				InlineMethods = inlineMethods.Get(),
				RemoveInlinedMethods = removeInlinedMethods.Get(),
				DecryptResources = decryptResources.Get(),
				DumpEmbeddedAssemblies = dumpEmbeddedAssemblies.Get(),
				RestoreFields = restoreFields.Get(),
				RenameResourceKeys = renameResourceKeys.Get(),
				CastDeobfuscation = castDeobfuscation.Get(),
			});
		}

		protected override IEnumerable<Option> GetOptionsInternal() {
			return new List<Option>() {
				inlineMethods,
				removeInlinedMethods,
				decryptResources,
				dumpEmbeddedAssemblies,
				restoreFields,
				renameResourceKeys,
				castDeobfuscation,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool startedDeobfuscating = false;

		StringDecrypter stringDecrypter;
		ResourceResolver resourceResolver;
		AssemblyResolver assemblyResolver;
		FieldsRestorer fieldsRestorer;
		ArrayBlockState arrayBlockState;

		internal class Options : OptionsBase {
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
			public bool DecryptResources { get; set; }
			public bool DumpEmbeddedAssemblies { get; set; }
			public bool RestoreFields { get; set; }
			public bool RenameResourceKeys { get; set; }
			public bool CastDeobfuscation { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		protected override bool CanInlineMethods {
			get { return startedDeobfuscating ? options.InlineMethods : true; }
		}

		public override IEnumerable<IBlocksDeobfuscator> BlocksDeobfuscators {
			get {
				var list = new List<IBlocksDeobfuscator>(GetBlocksDeobfuscators());
				if (CanInlineMethods)
					list.Add(new DsMethodCallInliner(new CachedCflowDeobfuscator(GetBlocksDeobfuscators())));
				return list;
			}
		}

		List<IBlocksDeobfuscator> GetBlocksDeobfuscators() {
			var list = new List<IBlocksDeobfuscator>();
			if (arrayBlockState != null && arrayBlockState.Detected)
				list.Add(new ArrayBlockDeobfuscator(arrayBlockState));
			if (!startedDeobfuscating || options.CastDeobfuscation)
				list.Add(new CastDeobfuscator());
			return list;
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;

			if (options.RenameResourceKeys)
				this.RenamingOptions |= RenamingOptions.RenameResourceKeys;
			else
				this.RenamingOptions &= ~RenamingOptions.RenameResourceKeys;
		}

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(stringDecrypter.Detected) +
					ToInt32(resourceResolver.Detected) +
					ToInt32(assemblyResolver.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void ScanForObfuscator() {
			staticStringInliner.UseUnknownArgs = true;
			arrayBlockState = new ArrayBlockState(module);
			arrayBlockState.Initialize(DeobfuscatedFile);
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.Find(DeobfuscatedFile);
			resourceResolver = new ResourceResolver(module, DeobfuscatedFile, this);
			resourceResolver.Find();
			assemblyResolver = new AssemblyResolver(module, DeobfuscatedFile, this);
			assemblyResolver.Find();
			obfuscatorName = DetectVersion();
		}

		string DetectVersion() {
			switch (stringDecrypter.Version) {
			case StringDecrypter.DecrypterVersion.V1_3:
				if (DetectMethodProxyObfuscation())
					return DeobfuscatorInfo.THE_NAME + " 3.5";
				return DeobfuscatorInfo.THE_NAME + " 1.x-3.x";
			case StringDecrypter.DecrypterVersion.V4_0:
				return DeobfuscatorInfo.THE_NAME + " 4.0";
			case StringDecrypter.DecrypterVersion.V4_1:
				return DeobfuscatorInfo.THE_NAME + " 4.1";
			}

			return DeobfuscatorInfo.THE_NAME;
		}

		bool DetectMethodProxyObfuscation() {
			const int MIN_FOUND_PROXIES = 10;

			int foundProxies = 0, checkedMethods = 0;
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (foundProxies >= MIN_FOUND_PROXIES)
						goto done;
					if (!method.IsStatic || method.Body == null)
						continue;
					if (checkedMethods++ >= 1000)
						goto done;
					if (!DsMethodCallInliner.CanInline(method))
						continue;
					foundProxies++;
				}
			}
done:
			return foundProxies >= MIN_FOUND_PROXIES;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			if (options.RestoreFields) {
				fieldsRestorer = new FieldsRestorer(module);
				fieldsRestorer.Initialize();
			}

			foreach (var method in stringDecrypter.DecrypterMethods) {
				staticStringInliner.Add(method, (method2, gim, args) => {
					return stringDecrypter.Decrypt(method2, args);
				});
			}
			DeobfuscatedFile.StringDecryptersAdded();

			resourceResolver.Initialize();
			DecryptResources();

			DumpEmbeddedAssemblies();

			startedDeobfuscating = true;
		}

		void DecryptResources() {
			if (!options.DecryptResources)
				return;
			EmbeddedResource rsrc;
			if (!resourceResolver.MergeResources(out rsrc))
				return;
			AddResourceToBeRemoved(rsrc, "Encrypted resources");
			AddCctorInitCallToBeRemoved(resourceResolver.InitMethod);
			AddCallToBeRemoved(module.EntryPoint, resourceResolver.InitMethod);
			AddMethodToBeRemoved(resourceResolver.InitMethod, "Resource resolver init method");
			AddMethodToBeRemoved(resourceResolver.InitMethod2, "Resource resolver init method #2");
			AddMethodToBeRemoved(resourceResolver.HandlerMethod, "Resource resolver handler method");
			AddMethodToBeRemoved(resourceResolver.GetDataMethod, "Resource resolver 'get resource data' method");
		}

		void DumpEmbeddedAssemblies() {
			if (!options.DumpEmbeddedAssemblies)
				return;
			foreach (var info in assemblyResolver.GetAssemblyInfos()) {
				if (info.resource != null && info.resource == resourceResolver.Resource)
					continue;
				DeobfuscatedFile.CreateAssemblyFile(info.data, info.simpleName, info.extension);
				AddResourceToBeRemoved(info.resource, string.Format("Embedded assembly: {0}", info.fullName));
			}
			AddCctorInitCallToBeRemoved(assemblyResolver.InitMethod);
			AddCallToBeRemoved(module.EntryPoint, assemblyResolver.InitMethod);
			AddMethodToBeRemoved(assemblyResolver.InitMethod, "Assembly resolver init method");
			AddMethodToBeRemoved(assemblyResolver.HandlerMethod, "Assembly resolver handler method");
			AddMethodToBeRemoved(assemblyResolver.DecryptMethod, "Assembly resolver decrypt method");
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			if (options.RestoreFields)
				fieldsRestorer.Deobfuscate(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			if (options.RestoreFields && CanRemoveTypes)
				fieldsRestorer.CleanUp();
			RemoveInlinedMethods();

			if (options.RestoreFields)
				AddTypesToBeRemoved(fieldsRestorer.FieldStructs, "Type with moved fields");

			if (CanRemoveStringDecrypterType) {
				AddMethodsToBeRemoved(stringDecrypter.DecrypterMethods, "String decrypter method");
				stringDecrypter.CleanUp();
			}

			AddFieldsToBeRemoved(arrayBlockState.CleanUp(), "Control flow obfuscation array");

			base.DeobfuscateEnd();
		}

		void RemoveInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;
			RemoveInlinedMethods(DsInlinedMethodsFinder.Find(module, staticStringInliner.Methods));
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var method in stringDecrypter.DecrypterMethods)
				list.Add(method.MDToken.ToInt32());
			return list;
		}
	}
}
