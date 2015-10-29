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
using AssemblyData;
using de4dot.code.AssemblyClient;
using dnlib.DotNet;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class DynamicDynocodeIterator : IDisposable, IEnumerable<int> {
		IAssemblyClient assemblyClient;
		List<TypeDef> dynocodeTypes = new List<TypeDef>();

		public List<TypeDef> Types {
			get { return dynocodeTypes; }
		}

		class MyEnumerator : IEnumerator<int> {
			DynamicDynocodeIterator ddi;

			public MyEnumerator(DynamicDynocodeIterator ddi) {
				this.ddi = ddi;
			}

			public int Current {
				get {
					return (int)ddi.assemblyClient.GenericService.SendMessage(DynocodeService.MSG_CALL_GET_CURRENT, null);
				}
			}

			public void Dispose() {
			}

			object System.Collections.IEnumerator.Current {
				get { return Current; }
			}

			public bool MoveNext() {
				return (bool)ddi.assemblyClient.GenericService.SendMessage(DynocodeService.MSG_CALL_MOVE_NEXT, null);
			}

			public void Reset() {
				throw new NotImplementedException();
			}
		}

		public void Dispose() {
			if (assemblyClient != null)
				assemblyClient.Dispose();
			assemblyClient = null;
		}

		public void Initialize(ModuleDef module) {
			if (assemblyClient != null)
				return;

			var serverVersion = NewProcessAssemblyClientFactory.GetServerClrVersion(module);
			assemblyClient = new NewProcessAssemblyClientFactory(serverVersion).Create(AssemblyServiceType.Generic);
			assemblyClient.Connect();
			assemblyClient.WaitConnected();

			assemblyClient.GenericService.LoadUserService(typeof(DynocodeService), null);
			assemblyClient.GenericService.LoadAssembly(module.Location);
		}

		public void CreateEnumerable(MethodDef ctor, object[] args) {
			var type = ctor.DeclaringType;
			while (type.DeclaringType != null)
				type = type.DeclaringType;
			dynocodeTypes.Add(type);
			assemblyClient.GenericService.SendMessage(DynocodeService.MSG_CREATE_ENUMERABLE,
					new object[] { ctor.MDToken.ToUInt32(), args });
		}

		public void WriteEnumerableField(uint fieldToken, object value) {
			assemblyClient.GenericService.SendMessage(DynocodeService.MSG_WRITE_ENUMERABLE_FIELD,
					new object[] { fieldToken, value });
		}

		public void CreateEnumerator() {
			assemblyClient.GenericService.SendMessage(DynocodeService.MSG_CREATE_ENUMERATOR, null);
		}

		public IEnumerator<int> GetEnumerator() {
			return new MyEnumerator(this);
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
			return new MyEnumerator(this);
		}
	}
}
