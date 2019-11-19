﻿// Copyright (c) 2019 Jon P Smith, GitHub: JonPSmith, web: http://www.thereformedprogrammer.net/
// Licensed under MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using DataLayer;
using EntityClasses;
using EntityClasses.DomainEvents;
using EntityClasses.SupportClasses;
using GenericEventRunner.ForEntities;
using GenericEventRunner.ForHandlers;
using Test.EfHelpers;
using TestSupport.EfHelpers;
using Xunit;
using Xunit.Extensions.AssertExtensions;

namespace Test.UnitTests.InfrastructureTests
{
    public class TestContextWithEventHandling
    {
        [Fact]
        public void TestCreateOrderCheckEventsProduced()
        {
            //SETUP
            var options = SqliteInMemory.CreateOptions<ExampleDbContext>();
            var context = options.CreateAndSeedDbWithDiForHandlers();
            {
                var itemDto = new BasketItemDto
                {
                    ProductCode = context.ProductStocks.OrderBy(x => x.NumInStock).First().ProductCode,
                    NumOrdered = 2,
                    ProductPrice = 123
                };

                //ATTEMPT
                var order = new Order("test", DateTime.Now, new List<BasketItemDto> {itemDto});

                //VERIFY
                order.TotalPriceNoTax.ShouldEqual(2*123);
                order.GetBeforeSaveEventsThenClear().Select(x => x.GetType())
                    .ShouldEqual(new []{typeof(OrderCreatedEvent), typeof(AllocateProductEvent)});
            }
        }

        [Fact]
        public void TestOrderCreatedHandler()
        {
            //SETUP
            var options = SqliteInMemory.CreateOptions<ExampleDbContext>();
            var context = options.CreateAndSeedDbWithDiForHandlers();
            {
                var itemDto = new BasketItemDto
                {
                    ProductCode = context.ProductStocks.OrderBy(x => x.NumInStock).First().ProductCode,
                    NumOrdered = 2,
                    ProductPrice = 123
                };

                //ATTEMPT
                var order = new Order("test", DateTime.Now, new List<BasketItemDto> { itemDto });
                context.Add(order);
                context.SaveChanges();

                //VERIFY
                order.TotalPriceNoTax.ShouldEqual(2 * 123);
                order.TaxRatePercent.ShouldEqual(4);
                order.GrandTotalPrice.ShouldEqual(order.TotalPriceNoTax * (1 + order.TaxRatePercent / 100));
                context.ProductStocks.OrderBy(x => x.NumInStock).First().NumAllocated.ShouldEqual(2);
            }
        }

        [Fact]
        public void TestOrderCreatedHandlerLogs()
        {
            //SETUP
            var options = SqliteInMemory.CreateOptions<ExampleDbContext>();
            var logs = new List<LogOutput>();
            var context = options.CreateAndSeedDbWithDiForHandlers(logs);
            {
                var itemDto = new BasketItemDto
                {
                    ProductCode = context.ProductStocks.OrderBy(x => x.NumInStock).First().ProductCode,
                    NumOrdered = 2,
                    ProductPrice = 123
                };

                //ATTEMPT
                var order = new Order("test", DateTime.Now, new List<BasketItemDto> { itemDto });
                context.Add(order);
                context.SaveChanges();

                //VERIFY
                logs.Count.ShouldEqual(3);
                logs[0].Message.ShouldEqual("About to run a BeforeSave event handler Infrastructure.BeforeEventHandlers.OrderCreatedHandler.");
                logs[1].Message.ShouldEqual("About to run a BeforeSave event handler Infrastructure.BeforeEventHandlers.AllocateProductHandler.");
                logs[2].Message.ShouldEqual("About to run a BeforeSave event handler Infrastructure.BeforeEventHandlers.TaxRateChangedHandler.");
            }
        }

        [Fact]
        public void TestOrderDispatchedHandler()
        {
            //SETUP
            var options = SqliteInMemory.CreateOptions<ExampleDbContext>();
            var context = options.CreateAndSeedDbWithDiForHandlers();
            {
                var itemDto = new BasketItemDto
                {
                    ProductCode = context.ProductStocks.OrderBy(x => x.NumInStock).First().ProductCode,
                    NumOrdered = 2,
                    ProductPrice = 123
                };
                var order = new Order("test", DateTime.Now, new List<BasketItemDto> { itemDto });
                context.Add(order);
                context.SaveChanges();

                //ATTEMPT
                order.OrderHasBeenDispatched(DateTime.Now.AddDays(10));
                context.SaveChanges();

                //VERIFY
                order.TotalPriceNoTax.ShouldEqual(2 * 123);
                order.TaxRatePercent.ShouldEqual(9);
                order.GrandTotalPrice.ShouldEqual(order.TotalPriceNoTax * (1 + order.TaxRatePercent / 100));
                context.ProductStocks.OrderBy(x => x.NumInStock).First().NumAllocated.ShouldEqual(0);
                context.ProductStocks.OrderBy(x => x.NumInStock).First().NumInStock.ShouldEqual(3);
            }
        }

        [Fact]
        public void TestOrderDispatchedHandlerLogs()
        {
            //SETUP
            var options = SqliteInMemory.CreateOptions<ExampleDbContext>();
            var logs = new List<LogOutput>();
            var context = options.CreateAndSeedDbWithDiForHandlers(logs);
            {
                var itemDto = new BasketItemDto
                {
                    ProductCode = context.ProductStocks.OrderBy(x => x.NumInStock).First().ProductCode,
                    NumOrdered = 2,
                    ProductPrice = 123
                };
                var order = new Order("test", DateTime.Now, new List<BasketItemDto> { itemDto });
                context.Add(order);
                context.SaveChanges();
                logs.Clear();

                //ATTEMPT
                order.OrderHasBeenDispatched(DateTime.Now.AddDays(10));
                context.SaveChanges();

                //VERIFY
                logs.Count.ShouldEqual(3);
                logs[0].Message.ShouldEqual("About to run a BeforeSave event handler Infrastructure.BeforeEventHandlers.OrderDispatchedBeforeHandler.");
                logs[1].Message.ShouldEqual("About to run a BeforeSave event handler Infrastructure.BeforeEventHandlers.TaxRateChangedHandler.");
                logs[2].Message.ShouldEqual("About to run a AfterSave event handler Infrastructure.AfterEventHandlers.OrderDispatchedAfterHandler.");
            }
        }

        private class EventWithNoHandler : IDomainEvent
        {
        }

        [Theory]
        [InlineData(EventToSend.Before)]
        [InlineData(EventToSend.After)]
        public void TestMissingHandlerThrowsException(EventToSend beforeAfter)
        {

            //SETUP
            var options = SqliteInMemory.CreateOptions<ExampleDbContext>();
            var context = options.CreateAndSeedDbWithDiForHandlers();
            {
                var itemDto = new BasketItemDto
                {
                    ProductCode = context.ProductStocks.OrderBy(x => x.NumInStock).First().ProductCode,
                    NumOrdered = 2,
                    ProductPrice = 123
                };
                var order = new Order("test", DateTime.Now, new List<BasketItemDto> { itemDto });
                context.Add(order);

                //ATTEMPT
                order.AddEvent(new EventWithNoHandler(), beforeAfter);
                var ex = Assert.Throws<GenericEventRunnerException>(() => context.SaveChanges());

                //VERIFY
                ex.Message.ShouldEqual(beforeAfter == EventToSend.Before
                    ? $"Could not find a BeforeSave event handler for the event {typeof(EventWithNoHandler).Name}."
                    : $"Could not find a AfterSave event handler for the event {typeof(EventWithNoHandler).Name}.");
            }
        }

    }
}