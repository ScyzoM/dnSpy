﻿/*
    Copyright (C) 2014-2017 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using dnSpy.Contracts.Debugger.DotNet.Evaluation;
using dnSpy.Contracts.Debugger.Engine.Evaluation;
using dnSpy.Contracts.Debugger.Evaluation;
using dnSpy.Debugger.DotNet.Mono.Impl.Evaluation;
using Mono.Debugger.Soft;

namespace dnSpy.Debugger.DotNet.Mono.Impl {
	sealed partial class DbgEngineImpl {
		internal DbgDotNetValue CreateDotNetValue_MonoDebug(ValueLocation valueLocation) {
			debuggerThread.VerifyAccess();
			var value = valueLocation.Load();
			if (value == null)
				return new SyntheticNullValue(valueLocation.Type);

			var dnValue = new DbgDotNetValueImpl(this, valueLocation, value);
			lock (lockObj)
				dotNetValuesToCloseOnContinue.Add(dnValue);
			return dnValue;
		}

		void CloseDotNetValues_MonoDebug() {
			debuggerThread.VerifyAccess();
			DbgDotNetValueImpl[] valuesToClose;
			lock (lockObj) {
				valuesToClose = dotNetValuesToCloseOnContinue.Count == 0 ? Array.Empty<DbgDotNetValueImpl>() : dotNetValuesToCloseOnContinue.ToArray();
				dotNetValuesToCloseOnContinue.Clear();
			}
			foreach (var value in valuesToClose)
				value.Dispose();
		}

		bool IsEvaluating => funcEvalFactory.IsEvaluating;
		internal int MethodInvokeCounter => funcEvalFactory.MethodInvokeCounter;

		sealed class EvalTimedOut { }

		internal DbgDotNetValueResult? CheckFuncEval(DbgEvaluationContext context) {
			debuggerThread.VerifyAccess();
			if (!IsPaused)
				return new DbgDotNetValueResult(PredefinedEvaluationErrorMessages.CanFuncEvalOnlyWhenPaused);
			if (isUnhandledException)
				return new DbgDotNetValueResult(PredefinedEvaluationErrorMessages.CantFuncEvalWhenUnhandledExceptionHasOccurred);
			if (context.ContinueContext.HasData<EvalTimedOut>())
				return new DbgDotNetValueResult(PredefinedEvaluationErrorMessages.FuncEvalTimedOutNowDisabled);
			if (IsEvaluating)
				return new DbgDotNetValueResult(PredefinedEvaluationErrorMessages.CantFuncEval);
			return null;
		}

		Value TryInvokeMethod(ThreadMirror thread, ObjectMirror obj, MethodMirror method, IList<Value> arguments, out bool timedOut) {
			debuggerThread.VerifyAccess();
			Debug.Assert(!IsEvaluating);
			var funcEvalTimeout = DbgLanguage.DefaultFuncEvalTimeout;
			const bool suspendOtherThreads = true;
			var cancellationToken = CancellationToken.None;
			try {
				timedOut = false;
				using (var funcEval = funcEvalFactory.CreateFuncEval(a => OnFuncEvalComplete(a), thread, funcEvalTimeout, suspendOtherThreads, cancellationToken))
					return funcEval.CallMethod(method, obj, arguments).Result;
			}
			catch (TimeoutException) {
				timedOut = true;
				return null;
			}
		}

		void OnFuncEvalComplete(FuncEval funcEval, DbgEvaluationContext context) {
			if (funcEval.EvalTimedOut)
				context.ContinueContext.GetOrCreateData<EvalTimedOut>();
			OnFuncEvalComplete(funcEval);
		}

		void OnFuncEvalComplete(FuncEval funcEval) {
		}
	}
}