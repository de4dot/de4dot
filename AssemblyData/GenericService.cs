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
using System.Reflection;

namespace AssemblyData {
	class GenericService : AssemblyService, IGenericService {
		IUserGenericService userGenericService;

		public override void Exit() {
			if (userGenericService != null)
				userGenericService.Dispose();
			userGenericService = null;
			base.Exit();
		}

		public void LoadUserService(Type createServiceType, object createMethodArgs) {
			var createServiceMethod = GetCreateUserServiceMethod(createServiceType);
			userGenericService = createServiceMethod.Invoke(null, null) as IUserGenericService;
			if (userGenericService == null)
				throw new ApplicationException("create-service-method failed to create user service");
		}

		MethodInfo GetCreateUserServiceMethod(Type createServiceType) {
			if (createServiceType == null)
				throw new ApplicationException("Create-service-type is null");
			foreach (var method in createServiceType.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
				if (method.GetCustomAttributes(typeof(CreateUserGenericServiceAttribute), false).Length > 0)
					return method;
			}
			throw new ApplicationException($"Failed to find create-service-method. Type token: Type: {createServiceType}");
		}

		void CheckUserService() {
			if (userGenericService == null)
				throw new ApplicationException("LoadUserService() hasn't been called yet.");
		}

		public void LoadAssembly(string filename) {
			CheckUserService();
			LoadAssemblyInternal(filename);
			userGenericService.AssemblyLoaded(assembly);
		}

		public object SendMessage(int msg, object[] args) {
			CheckUserService();
			return userGenericService.HandleMessage(msg, args);
		}
	}
}
