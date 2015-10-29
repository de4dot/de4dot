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
using System.Text;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.MaxtoCode {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "MaxtoCode";
		public const string THE_TYPE = "mc";
		const string DEFAULT_REGEX = @"!^[oO01l]+$&!^[A-F0-9]{20,}$&" + DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		IntOption stringCodePage;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			stringCodePage = new IntOption(null, MakeArgName("cp"), "String code page", 936);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator CreateDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				RenameResourcesInCode = false,
				ValidNameRegex = validNameRegex.Get(),
				StringCodePage = stringCodePage.Get(),
			});
		}

		protected override IEnumerable<Option> GetOptionsInternal() {
			return new List<Option>() {
				stringCodePage,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		MainType mainType;
		DecrypterInfo decrypterInfo;
		StringDecrypter stringDecrypter;

		internal class Options : OptionsBase {
			public int StringCodePage { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		internal Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			StringFeatures = StringFeatures.AllowStaticDecryption | StringFeatures.AllowDynamicDecryption;
		}

		protected override int DetectInternal() {
			int val = 0;

			if (mainType.Detected)
				val = 150;

			return val;
		}

		protected override void ScanForObfuscator() {
			mainType = new MainType(module);
			mainType.Find();
		}

		public override bool GetDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || !mainType.Detected)
				return false;

			var fileData = DeobUtils.ReadModule(module);
			decrypterInfo = new DecrypterInfo(mainType, fileData);
			var methodsDecrypter = new MethodsDecrypter(module, decrypterInfo);

			if (!methodsDecrypter.Decrypt(ref dumpedMethods))
				return false;

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator ModuleReloaded(ModuleDefMD module) {
			var newOne = new Deobfuscator(options);
			newOne.SetModule(module);
			newOne.mainType = new MainType(module, mainType);
			newOne.decrypterInfo = decrypterInfo;
			decrypterInfo = null;
			if (newOne.decrypterInfo != null)
				newOne.decrypterInfo.mainType = newOne.mainType;
			return newOne;
		}

		void FreePEImage() {
			if (decrypterInfo != null)
				decrypterInfo.Dispose();
			decrypterInfo = null;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			stringDecrypter = new StringDecrypter(decrypterInfo);
			stringDecrypter.Find();
			if (stringDecrypter.Detected) {
				stringDecrypter.Initialize(GetEncoding(options.StringCodePage));
				staticStringInliner.Add(stringDecrypter.Method, (method, gim, args) => stringDecrypter.Decrypt((uint)args[0]));
				DeobfuscatedFile.StringDecryptersAdded();
			}
			else
				FreePEImage();

			foreach (var method in mainType.InitMethods)
				AddCctorInitCallToBeRemoved(method);
			AddTypeToBeRemoved(mainType.Type, "Obfuscator type");
			RemoveDuplicateEmbeddedResources();
			RemoveInvalidResources();
		}

		public override void DeobfuscateEnd() {
			FreePEImage();
			base.DeobfuscateEnd();
		}

		static Encoding GetEncoding(int cp) {
			try {
				return Encoding.GetEncoding(cp);
			}
			catch {
				Logger.w("Invalid code page {0}!", cp);
				return Encoding.Default;
			}
		}

		class ResourceKey {
			readonly EmbeddedResource resource;

			public ResourceKey(EmbeddedResource resource) {
				this.resource = resource;
			}

			public override int GetHashCode() {
				int hash = 0;
				if (resource.Offset != null)
					hash ^= resource.Offset.GetHashCode();
				hash ^= (int)resource.Data.Position;
				hash ^= (int)resource.Data.Length;
				return hash;
			}

			public override bool Equals(object obj) {
				var other = obj as ResourceKey;
				if (other == null)
					return false;
				return resource.Data.FileOffset == other.resource.Data.FileOffset &&
					resource.Data.Length == other.resource.Data.Length;
			}

			public override string ToString() {
				return resource.Name.String;
			}
		}

		void RemoveDuplicateEmbeddedResources() {
			var resources = new Dictionary<ResourceKey, List<EmbeddedResource>>();
			foreach (var tmp in module.Resources) {
				var rsrc = tmp as EmbeddedResource;
				if (rsrc == null)
					continue;
				if (rsrc.Offset == null)
					continue;
				List<EmbeddedResource> list;
				var key = new ResourceKey(rsrc);
				if (!resources.TryGetValue(key, out list))
					resources[key] = list = new List<EmbeddedResource>();
				list.Add(rsrc);
			}

			foreach (var list in resources.Values) {
				if (list.Count <= 1)
					continue;

				EmbeddedResource resourceToKeep = null;
				foreach (var rsrc in list) {
					if (UTF8String.IsNullOrEmpty(rsrc.Name))
						continue;

					resourceToKeep = rsrc;
					break;
				}
				if (resourceToKeep == null)
					continue;

				foreach (var rsrc in list) {
					if (rsrc == resourceToKeep)
						continue;
					AddResourceToBeRemoved(rsrc, string.Format("Duplicate of resource {0}", Utils.ToCsharpString(resourceToKeep.Name)));
				}
			}
		}

		void RemoveInvalidResources() {
			foreach (var tmp in module.Resources) {
				var resource = tmp as EmbeddedResource;
				if (resource == null)
					continue;
				if (resource.Offset == null || (resource.Data.FileOffset == 0 && resource.Data.Length == 0))
					AddResourceToBeRemoved(resource, "Invalid resource");
			}
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter != null && stringDecrypter.Detected)
				list.Add(stringDecrypter.Method.MDToken.ToInt32());
			return list;
		}

		protected override void Dispose(bool disposing) {
			if (disposing)
				FreePEImage();
			base.Dispose(disposing);
		}
	}
}
