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

using System.Collections.Generic;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Eazfuscator.NET";
		public const string THE_TYPE = "ef";
		const string DEFAULT_REGEX = @"!^#=&!^dje_.+_ejd$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
			});
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool detectedVersion = false;

		DecrypterType decrypterType;
		StringDecrypter stringDecrypter;
		AssemblyResolver assemblyResolver;
		ResourceResolver resourceResolver;
		ResourceMethodsRestorer resourceMethodsRestorer;

		internal class Options : OptionsBase {
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

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(stringDecrypter.Detected) +
					toInt32(assemblyResolver.Detected) +
					toInt32(resourceResolver.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (detectedVersion)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			decrypterType = new DecrypterType(module, DeobfuscatedFile);
			stringDecrypter = new StringDecrypter(module, decrypterType);
			stringDecrypter.find();
			assemblyResolver = new AssemblyResolver(module, decrypterType);
			assemblyResolver.find();
			resourceResolver = new ResourceResolver(module, assemblyResolver);
			resourceResolver.find();
			if (stringDecrypter.Detected)
				detectVersion();
		}

		void detectVersion() {
			var version = new VersionDetector(module, stringDecrypter).detect();
			if (version == null)
				return;

			detectedVersion = true;
			obfuscatorName = DeobfuscatorInfo.THE_NAME + " " +  version;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			stringDecrypter.initialize(DeobfuscatedFile);
			staticStringInliner.add(stringDecrypter.Method, (method2, gim, args) => {
				return stringDecrypter.decrypt((int)args[0]);
			});
			DeobfuscatedFile.stringDecryptersAdded();

			assemblyResolver.initialize(DeobfuscatedFile, this);
			assemblyResolver.initializeEmbeddedFiles();
			addModuleCctorInitCallToBeRemoved(assemblyResolver.InitMethod);

			resourceResolver.initialize(DeobfuscatedFile, this);
			foreach (var info in resourceResolver.mergeResources())
				addResourceToBeRemoved(info.Resource, "Encrypted resources");
			addModuleCctorInitCallToBeRemoved(resourceResolver.InitMethod);

			resourceMethodsRestorer = new ResourceMethodsRestorer(module);
			resourceMethodsRestorer.find(DeobfuscatedFile, this);

			dumpEmbeddedAssemblies();
		}

		void dumpEmbeddedAssemblies() {
			foreach (var info in assemblyResolver.AssemblyInfos) {
				DeobfuscatedFile.createAssemblyFile(info.Data, info.SimpleName, info.Extension);
				addResourceToBeRemoved(info.Resource, string.Format("Embedded assembly: {0}", info.AssemblyFullName));
			}
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			resourceMethodsRestorer.deobfuscate(blocks);
			assemblyResolver.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			if (CanRemoveStringDecrypterType) {
				addTypesToBeRemoved(stringDecrypter.Types, "String decrypter type");
				addTypeToBeRemoved(decrypterType.Type, "Decrypter type");
				addResourceToBeRemoved(stringDecrypter.Resource, "Encrypted strings");
			}
			addTypeToBeRemoved(assemblyResolver.Type, "Assembly resolver type");
			addTypeToBeRemoved(assemblyResolver.OtherType, "Assembly resolver other type");
			addTypeToBeRemoved(resourceResolver.Type, "Resource resolver type");
			addTypeToBeRemoved(resourceMethodsRestorer.Type, "GetManifestResourceStream type");
			addResourceToBeRemoved(resourceMethodsRestorer.Resource, "GetManifestResourceStream type resource");

			fixInterfaces();
			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MetadataToken.ToInt32());
			return list;
		}
	}
}
