#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using DynamicData.Binding;
using DynamicData.Kernel;
using DynamicData.Tests.Domain;
using FluentAssertions;
using Xunit;

#endregion

namespace DynamicData.Tests.Cache
{
    
    public class SortFixture: IDisposable
    {
        private readonly ISourceCache<Person, string> _source;
        private readonly SortedChangeSetAggregator<Person, string> _results;
        private readonly RandomPersonGenerator _generator = new RandomPersonGenerator();
        private readonly IComparer<Person> _comparer;


        public SortFixture()
        {
            _comparer = SortExpressionComparer<Person>.Ascending(p => p.Name).ThenByAscending(p => p.Age);

            _source = new SourceCache<Person, string>(p => p.Key);
            _results = new SortedChangeSetAggregator<Person, string>
                (
                _source.Connect().Sort(_comparer)  
                );
        }

        public void Dispose()
        {
            _source.Dispose();
            _results.Dispose();
        }


        [Fact]
        public void DoesNotThrow1()
        {
            var cache = new SourceCache<Data, int>(d => d.Id);
            var sortPump = new Subject<Unit>();
            var disposable = cache.Connect()
                .Sort(SortExpressionComparer<Data>.Ascending(d => d.Id), sortPump)
                .Subscribe();

            disposable.Dispose();
        }

        [Fact]
        public void DoesNotThrow2()
        {
            var cache = new SourceCache<Data, int>(d => d.Id);
            var disposable = cache.Connect()
                .Sort(new BehaviorSubject<IComparer<Data>>(SortExpressionComparer<Data>.Ascending(d => d.Id)))
                .Subscribe();

            disposable.Dispose();
        }
        public class Data
        {
            public Data(int id, string value)
            {
                Id = id;
                Value = value;
            }

            public int Id { get; }
            public string Value { get; }
        }

        public class TestString : IEquatable<TestString>
        {
            private readonly string _name;

            public TestString(string name)
            {
                _name = name;
            }

            public static implicit operator TestString(string source)
            {
                return new TestString(source);
            }

            public static implicit operator string (TestString source)
            {
                return source._name;
            }

            public bool Equals(TestString other)
            {
                return StringComparer.InvariantCultureIgnoreCase.Equals(_name, other._name);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as TestString);
            }

            public override int GetHashCode()
            {
                return StringComparer.InvariantCultureIgnoreCase.GetHashCode(_name);
            }
        }

        public class ViewModel
        {
            public string Name { get; set; }

            public ViewModel(string name)
            {
                this.Name = name;
            }

            public class Comparer : IComparer<ViewModel>
            {
                public int Compare(ViewModel x, ViewModel y)
                {
                    return StringComparer.InvariantCultureIgnoreCase.Compare(x.Name, y.Name);
                }
            }
        }

        [Fact]
        public void SortAfterFilter()
        {
            var source = new SourceCache<Person, string>(p => p.Key);

            var filterSubject = new BehaviorSubject<Func<Person, bool>>(p => true);

            var agg = new SortedChangeSetAggregator<ViewModel, TestString>(source.Connect()
                                                                                 .Filter(filterSubject)
                                                                                 .Group(x => (TestString)x.Key)
                                                                                 .Transform(x => new ViewModel(x.Key))
                                                                                 .Sort(new ViewModel.Comparer()));

            source.Edit(x =>
            {
                x.AddOrUpdate(new Person("A", 1, "F"));
                x.AddOrUpdate(new Person("a", 1, "M"));
                x.AddOrUpdate(new Person("B", 1, "F"));
                x.AddOrUpdate(new Person("b", 1, "M"));
            });

            filterSubject.OnNext(p => p.Name.Equals("a", StringComparison.InvariantCultureIgnoreCase));
        }

        [Fact]
        public void SortAfterFilterList()
        {
            var source = new SourceList<Person>();

            var filterSubject = new BehaviorSubject<Func<Person, bool>>(p => true);

            var agg = source.Connect()
                            .Filter(filterSubject)
                            .Transform(x => new ViewModel(x.Name))
                            .Sort(new ViewModel.Comparer())
                            .AsAggregator();

            source.Edit(x =>
            {
                x.Add(new Person("A", 1, "F"));
                x.Add(new Person("a", 1, "M"));
                x.Add(new Person("B", 1, "F"));
                x.Add(new Person("b", 1, "M"));
            });

            filterSubject.OnNext(p => p.Name.Equals("a", StringComparison.InvariantCultureIgnoreCase));
        }

        [Fact]
        public void SortInitialBatch()
        {
            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            _results.Data.Count.Should().Be(100, "Should be 100 people in the cache");

            var expectedResult = people.OrderBy(p => p, _comparer).Select(p => new KeyValuePair<string, Person>(p.Name, p)).ToList();
            var actualResult = _results.Messages[0].SortedItems.ToList();

            actualResult.ShouldAllBeEquivalentTo(expectedResult);
        }

        [Fact]
        public void AppendAtBeginning()
        {
            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            //create age 0 to ensure it is inserted first
            var insert = new Person("_Aaron", 0);

            _source.AddOrUpdate(insert);

            _results.Data.Count.Should().Be(101, "Should be 101 people in the cache");
            var indexedItem = _results.Messages[1].SortedItems.Indexed().Lookup("_Aaron");

            indexedItem.HasValue.Should().BeTrue();
            indexedItem.Value.Index.Should().Be(0, "Inserted item should have index of zero");
        }

        [Fact]
        public void AppendInMiddle()
        {
            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            //create age 0 to ensure it is inserted first
            var insert = new Person("Marvin", 50);

            _source.AddOrUpdate(insert);

            _results.Data.Count.Should().Be(101, "Should be 101 people in the cache");
            var indexedItem = _results.Messages[1].SortedItems.Indexed().Lookup("Marvin");

            indexedItem.HasValue.Should().BeTrue();

            var list = _results.Messages[1].SortedItems.ToList();
            var sortedResult = list.OrderBy(p => _comparer).ToList();
            list.ShouldAllBeEquivalentTo(sortedResult);
        }

        [Fact]
        public void AppendAtEnd()
        {
            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            //create age 0 to ensure it is inserted first
            var insert = new Person("zzzzz", 1000);

            _source.AddOrUpdate(insert);

            _results.Data.Count.Should().Be(101, "Should be 101 people in the cache");
            var indexedItem = _results.Messages[1].SortedItems.Indexed().Lookup("zzzzz");

            indexedItem.HasValue.Should().BeTrue();

            var list = _results.Messages[1].SortedItems.ToList();
            var sortedResult = list.OrderBy(p => _comparer).ToList();
            list.ShouldAllBeEquivalentTo(sortedResult);
        }

        [Fact]
        public void RemoveFirst()
        {
            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            //create age 0 to ensure it is inserted first
            var remove = _results.Messages[0].SortedItems.First();

            _source.Remove(remove.Key);

            _results.Data.Count.Should().Be(99, "Should be 99 people in the cache");
            //TODO: fixed Text
            var indexedItem = _results.Messages[1].SortedItems.Indexed().Lookup(remove.Key);
            indexedItem.HasValue.Should().BeFalse();

            var list = _results.Messages[1].SortedItems.ToList();
            var sortedResult = list.OrderBy(p => _comparer).ToList();
            list.ShouldAllBeEquivalentTo(sortedResult);
        }

        [Fact]
        public void RemoveFromMiddle()
        {
            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            //create age 0 to ensure it is inserted first
            var remove = _results.Messages[0].SortedItems.Skip(50).First();

            _source.Remove(remove.Key);

            _results.Data.Count.Should().Be(99, "Should be 99 people in the cache");

            //TODO: fixed Text
            var indexedItem = _results.Messages[1].SortedItems.Indexed().Lookup(remove.Key);
            indexedItem.HasValue.Should().BeFalse();

            var list = _results.Messages[1].SortedItems.ToList();
            var sortedResult = list.OrderBy(p => _comparer).ToList();
            list.ShouldAllBeEquivalentTo(sortedResult);
        }

        [Fact]
        public void RemoveFromEnd()
        {
            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            //create age 0 to ensure it is inserted first
            var remove = _results.Messages[0].SortedItems.Last();

            _source.Remove(remove.Key);

            _results.Data.Count.Should().Be(99, "Should be 99 people in the cache");

            var indexedItem = _results.Messages[1].SortedItems.Indexed().Lookup(remove.Key);
            indexedItem.HasValue.Should().BeFalse();

            var list = _results.Messages[1].SortedItems.ToList();
            var sortedResult = list.OrderBy(p => _comparer).ToList();
            list.ShouldAllBeEquivalentTo(sortedResult);
        }

        [Fact]
        public void UpdateFirst()
        {
            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            var toupdate = _results.Messages[0].SortedItems.First().Value;
            var update = new Person(toupdate.Name, toupdate.Age + 5);

            _source.AddOrUpdate(update);

            _results.Data.Count.Should().Be(100, "Should be 100 people in the cache");
            //TODO: fixed Text
            var indexedItem = _results.Messages[1].SortedItems.Indexed().Lookup(update.Key);
            indexedItem.HasValue.Should().BeTrue();
            ReferenceEquals(update, indexedItem.Value.Value).Should().BeTrue();
            var list = _results.Messages[1].SortedItems.ToList();
            var sortedResult = list.OrderBy(p => _comparer).ToList();
            list.ShouldAllBeEquivalentTo(sortedResult);
        }

        [Fact]
        public void UpdateMiddle()
        {
            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            var toupdate = _results.Messages[0].SortedItems.Skip(50).First().Value;
            var update = new Person(toupdate.Name, toupdate.Age + 5);

            _source.AddOrUpdate(update);

            _results.Data.Count.Should().Be(100, "Should be 100 people in the cache");

            var indexedItem = _results.Messages[1].SortedItems.Indexed().Lookup(update.Key);

            indexedItem.HasValue.Should().BeTrue();
            ReferenceEquals(update, indexedItem.Value.Value).Should().BeTrue();
            var list = _results.Messages[1].SortedItems.ToList();
            var sortedResult = list.OrderBy(p => _comparer).ToList();
            list.ShouldAllBeEquivalentTo(sortedResult);
        }

        [Fact]
        public void UpdateLast()
        {
            //TODO: fixed Text

            var people = _generator.Take(100).ToArray();
            _source.AddOrUpdate(people);

            var toupdate = _results.Messages[0].SortedItems.Last().Value;
            var update = new Person(toupdate.Name, toupdate.Age + 5);

            _source.AddOrUpdate(update);

            _results.Data.Count.Should().Be(100, "Should be 100 people in the cache");
            var indexedItem = _results.Messages[1].SortedItems.Indexed().Lookup(update.Key);

            indexedItem.HasValue.Should().BeTrue();
            ReferenceEquals(update, indexedItem.Value.Value).Should().BeTrue();
            var list = _results.Messages[1].SortedItems.ToList();
            var sortedResult = list.OrderBy(p => _comparer).ToList();
            list.ShouldAllBeEquivalentTo(sortedResult);
        }

        [Fact]
        public void BatchUpdate1()
        {
            var people = _generator.Take(10).ToArray();
            _source.AddOrUpdate(people);
            var list = new ObservableCollectionExtended<Person>(people.OrderBy(p => p, _comparer));

            var toupdate = people[3];

            _source.Edit(updater =>
            {
                updater.Remove(people[0].Key);
                updater.Remove(people[1].Key);
                updater.AddOrUpdate(new Person(toupdate.Name, toupdate.Age - 24));
                updater.Remove(people[7]);
            });

            var adaptor = new SortedObservableCollectionAdaptor<Person, string>();

            adaptor.Adapt(_results.Messages.Last(), list);

            var shouldbe = _results.Messages.Last().SortedItems.Select(p => p.Value).ToList();
            list.ShouldAllBeEquivalentTo(shouldbe);
        }

        [Fact]
        public void BatchUpdateWhereUpdateMovesTheIndexDown()
        {
            var people = _generator.Take(10).ToArray();
            _source.AddOrUpdate(people);

            var toupdate = people[3];

            _source.Edit(updater =>
            {
                updater.Remove(people[0].Key);
                updater.Remove(people[1].Key);

                updater.AddOrUpdate(new Person(toupdate.Name, toupdate.Age + 50));

                updater.AddOrUpdate(_generator.Take(2));

                updater.Remove(people[7]);
            });

            var list = new ObservableCollectionExtended<Person>(people.OrderBy(p => p, _comparer));
            var adaptor = new SortedObservableCollectionAdaptor<Person, string>();
            adaptor.Adapt(_results.Messages.Last(), list);

            var shouldbe = _results.Messages.Last().SortedItems.Select(p => p.Value).ToList();
            list.ShouldAllBeEquivalentTo(shouldbe);
        }

        [Fact]
        public void BatchUpdate2()
        {
            var people = _generator.Take(10).ToArray();
            _source.AddOrUpdate(people);

            var list = new ObservableCollectionExtended<Person>(people.OrderBy(p => p, _comparer));

            var toupdate = people[3];

            _source.Edit(updater =>
            {
                updater.AddOrUpdate(new Person(toupdate.Name, toupdate.Age - 24));
                updater.AddOrUpdate(new Person("Mr", "Z", 50, "M"));
            });

            var adaptor = new SortedObservableCollectionAdaptor<Person, string>();

            adaptor.Adapt(_results.Messages.Last(), list);

            var shouldbe = _results.Messages.Last().SortedItems.Select(p => p.Value).ToList();
            list.ShouldAllBeEquivalentTo(shouldbe);
        }

        [Fact]
        public void BatchUpdate3()
        {
            var people = _generator.Take(10).ToArray();
            _source.AddOrUpdate(people);
            var list = new ObservableCollectionExtended<Person>(people.OrderBy(p => p, _comparer));

            var toupdate = people[7];

            _source.Edit(updater =>
            {
                updater.AddOrUpdate(new Person(toupdate.Name, toupdate.Age - 24));
                updater.AddOrUpdate(new Person("Mr", "A", 10, "M"));
                updater.AddOrUpdate(new Person("Mr", "B", 40, "M"));
                updater.AddOrUpdate(new Person("Mr", "C", 70, "M"));
            });

            var adaptor = new SortedObservableCollectionAdaptor<Person, string>();

            adaptor.Adapt(_results.Messages.Last(), list);

            var shouldbe = _results.Messages.Last().SortedItems.Select(p => p.Value).ToList();
            list.ShouldAllBeEquivalentTo(shouldbe);
        }

        [Fact]
        public void BatchUpdate4()
        {
            var people = _generator.Take(10).ToArray();
            _source.AddOrUpdate(people);

            var list = new ObservableCollectionExtended<Person>(people.OrderBy(p => p, _comparer));

            var toupdate = people[3];

            _source.Edit(updater =>
            {
                updater.AddOrUpdate(new Person(toupdate.Name, toupdate.Age - 24));
                updater.AddOrUpdate(new Person("Mr", "A", 10, "M"));
                updater.Remove(people[5]);
                updater.AddOrUpdate(new Person("Mr", "C", 70, "M"));
            });

            var adaptor = new SortedObservableCollectionAdaptor<Person, string>();

            adaptor.Adapt(_results.Messages.Last(), list);

            var shouldbe = _results.Messages.Last().SortedItems.Select(p => p.Value).ToList();
            list.ShouldAllBeEquivalentTo(shouldbe);
        }

        [Fact]
        public void BatchUpdate6()
        {
            var people = _generator.Take(10).ToArray();
            _source.AddOrUpdate(people);

            _source.Edit(updater =>
            {
                updater.Clear();
                updater.AddOrUpdate(_generator.Take(10).ToArray());
                updater.Clear();
            });

            var list = new ObservableCollectionExtended<Person>(people.OrderBy(p => p, _comparer));

            var adaptor = new SortedObservableCollectionAdaptor<Person, string>();

            adaptor.Adapt(_results.Messages.Last(), list);

            var shouldbe = _results.Messages.Last().SortedItems.Select(p => p.Value).ToList();
            list.ShouldAllBeEquivalentTo(shouldbe);
        }

        [Fact]
        public void InlineUpdateProducesAReplace()
        {
            var people = _generator.Take(10).ToArray();
            _source.AddOrUpdate(people);
            var toupdate = people[3];

            _source.AddOrUpdate(new Person(toupdate.Name, toupdate.Age + 1));

            var list = new ObservableCollectionExtended<Person>(people.OrderBy(p => p, _comparer));
            var adaptor = new SortedObservableCollectionAdaptor<Person, string>();
            adaptor.Adapt(_results.Messages.Last(), list);

            var shouldbe = _results.Messages.Last().SortedItems.Select(p => p.Value).ToList();
            list.ShouldAllBeEquivalentTo(shouldbe);
        }
    }
}
