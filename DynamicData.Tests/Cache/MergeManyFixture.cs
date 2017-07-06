using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Xunit;
using FluentAssertions;

namespace DynamicData.Tests.Cache
{
    
    public class MergeManyFixture: IDisposable
    {
        private class ObjectWithObservable
        {
            private readonly ISubject<bool> _changed = new Subject<bool>();
            private bool _value;

            public ObjectWithObservable(int id)
            {
                Id = id;
            }

            public void InvokeObservable(bool value)
            {
                _value = value;
                _changed.OnNext(value);
            }

            public IObservable<bool> Observable => _changed.AsObservable();

            public int Id { get; }
        }

        private readonly SourceCache<ObjectWithObservable, int> _source;

        public  MergeManyFixture()
        {
            _source = new SourceCache<ObjectWithObservable, int>(p => p.Id);
        }

        public void Dispose()
        {
            _source.Dispose();
        }

        /// <summary>
        /// Invocations the only when child is invoked.
        /// </summary>
        [Fact]
        public void InvocationOnlyWhenChildIsInvoked()
        {
            bool invoked = false;

            var stream = _source.Connect()
                                .MergeMany(o => o.Observable)
                                .Subscribe(o => { invoked = true; });

            var item = new ObjectWithObservable(1);
            _source.AddOrUpdate(item);

            invoked.Should().BeFalse();

            item.InvokeObservable(true);
            invoked.Should().BeTrue();
            stream.Dispose();
        }

        [Fact]
        public void RemovedItemWillNotCauseInvocation()
        {
            bool invoked = false;
            var stream = _source.Connect()
                .MergeMany(o => o.Observable)
                .Subscribe(o => { invoked = true; });

            var item = new ObjectWithObservable(1);
            _source.AddOrUpdate(item);
            _source.Remove(item);
            invoked.Should().BeFalse();

            item.InvokeObservable(true);
            invoked.Should().BeFalse();
            stream.Dispose();
        }

        [Fact]
        public void EverythingIsUnsubscribedWhenStreamIsDisposed()
        {
            bool invoked = false;
            var stream = _source.Connect()
                .MergeMany(o => o.Observable)
                .Subscribe(o => { invoked = true; });

            var item = new ObjectWithObservable(1);
            _source.AddOrUpdate(item);

            stream.Dispose();

            item.InvokeObservable(true);
            invoked.Should().BeFalse();
        }
    }
}
