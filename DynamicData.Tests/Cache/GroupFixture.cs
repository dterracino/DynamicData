﻿using System;
using System.Linq;
using System.Reactive.Linq;
using DynamicData.Tests.Domain;
using FluentAssertions;
using Xunit;

namespace DynamicData.Tests.Cache
{
    
    public class GroupFixture: IDisposable
    {
        public  GroupFixture()
        {
            _source = new SourceCache<Person, string>(p => p.Name);
        }

        public void Dispose()
        {
            _source.Dispose();
        }

        private readonly ISourceCache<Person, string> _source;

        [Fact]
        public void Add()
        {
            bool called = false;
            IDisposable subscriber = _source.Connect().Group(p => p.Age)
                                            .Subscribe(
                                                updates =>
                                                {
                                                    updates.Count.Should().Be(1, "Should be 1 add");
                                                    updates.First().Reason.Should().Be(ChangeReason.Add);
                                                    called = true;
                                                });
            _source.AddOrUpdate(new Person("Person1", 20));

            subscriber.Dispose();
            called.Should().BeTrue();
        }

        [Fact]
        public void UpdateNotPossible()
        {
            bool called = false;
            IDisposable subscriber = _source.Connect().Group(p => p.Age).Skip(1)
                                            .Subscribe(updates => { called = true; });
            _source.AddOrUpdate(new Person("Person1", 20));
            _source.AddOrUpdate(new Person("Person1", 20));
            subscriber.Dispose();
            called.Should().BeFalse();
        }

        [Fact]
        public void UpdateAnItemWillChangedThegroup()
        {
            bool called = false;
            IDisposable subscriber = _source.Connect().Group(p => p.Age)
                                            .Subscribe(updates => { called = true; });
            _source.AddOrUpdate(new Person("Person1", 20));
            _source.AddOrUpdate(new Person("Person1", 21));
            subscriber.Dispose();
            called.Should().BeTrue();
        }

        [Fact]
        public void Remove()
        {
            bool called = false;
            IDisposable subscriber = _source.Connect().Group(p => p.Age)
                                            .Skip(1)
                                            .Subscribe(
                                                updates =>
                                                {
                                                    updates.Count.Should().Be(1, "Should be 1 add");
                                                    updates.First().Reason.Should().Be(ChangeReason.Remove);
                                                    called = true;
                                                });
            _source.AddOrUpdate(new Person("Person1", 20));
            _source.Remove(new Person("Person1", 20));
            subscriber.Dispose();
            called.Should().BeTrue();
        }

        [Fact]
        public void FiresCompletedWhenDisposed()
        {
            bool completed = false;
            IDisposable subscriber = _source.Connect().Group(p => p.Age)
                                            .Subscribe(updates => { },
                                                       () => { completed = true; });
            _source.Dispose();
            subscriber.Dispose();
            completed.Should().BeTrue();
        }

        [Fact]
        public void FiresManyValueForBatchOfDifferentAdds()
        {
            bool called = false;
            IDisposable subscriber = _source.Connect().Group(p => p.Age)
                                            .Subscribe(
                                                updates =>
                                                {
                                                    updates.Count.Should().Be(4, "Should be 4 adds");
                                                    foreach (var update in updates)
                                                    {
                                                        update.Reason.Should().Be(ChangeReason.Add);
                                                    }
                                                    called = true;
                                                });
            _source.Edit(updater =>
            {
                updater.AddOrUpdate(new Person("Person1", 20));
                updater.AddOrUpdate(new Person("Person2", 21));
                updater.AddOrUpdate(new Person("Person3", 22));
                updater.AddOrUpdate(new Person("Person4", 23));
            });

            subscriber.Dispose();
            called.Should().BeTrue();
        }

        [Fact]
        public void FiresOnlyOnceForABatchOfUniqueValues()
        {
            bool called = false;
            IDisposable subscriber = _source.Connect().Group(p => p.Age)
                                            .Subscribe(
                                                updates =>
                                                {
                                                    updates.Count.Should().Be(1, "Should be 1 add");
                                                    updates.First().Reason.Should().Be(ChangeReason.Add);
                                                    called = true;
                                                });
            _source.Edit(updater =>
            {
                updater.AddOrUpdate(new Person("Person1", 20));
                updater.AddOrUpdate(new Person("Person2", 20));
                updater.AddOrUpdate(new Person("Person3", 20));
                updater.AddOrUpdate(new Person("Person4", 20));
            });

            subscriber.Dispose();
            called.Should().BeTrue();
        }

        [Fact]
        public void FiresRemoveWhenEmptied()
        {
            bool called = false;
            //skip first one a this is setting up the stream
            IDisposable subscriber = _source.Connect().Group(p => p.Age).Skip(1)
                                            .Subscribe(
                                                updates =>
                                                {
                                                    updates.Count.Should().Be(1, "Should be 1 update");
                                                    foreach (var update in updates)
                                                    {
                                                        update.Reason.Should().Be(ChangeReason.Remove);
                                                    }
                                                    called = true;
                                                });
            var person = new Person("Person1", 20);

            _source.AddOrUpdate(person);

            //remove
            _source.Remove(person);

            subscriber.Dispose();
            called.Should().BeTrue();
        }

        [Fact]
        public void ReceivesUpdateWhenFeederIsInvoked()
        {
            bool called = false;
            var subscriber = _source.Connect().Group(p => p.Age)
                                    .Subscribe(updates => { called = true; });
            _source.AddOrUpdate(new Person("Person1", 20));
            subscriber.Dispose();
            called.Should().BeTrue();
        }
    }
}
