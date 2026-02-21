// See https://aka.ms/new-console-template for more information

using AsyncStateMachine;
using AsyncStateMachine.Tests;

var factory = new StateFactory<DummyContext>();
factory.Register(() => new DummyState());
factory.Register(() => new CloseState());

var stateMachine = StateMachine<DummyContext>.Create(new DummyContext(), factory);
stateMachine.SetInitialState<DummyState>();

await stateMachine.RunAsync();