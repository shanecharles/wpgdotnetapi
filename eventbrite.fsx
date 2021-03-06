﻿#r "packages/Suave/lib/net40/Suave.dll"
#r "packages/FSharp.Data/lib/net40/FSharp.Data.dll"
#r "packages/FSharpX.Extras/lib/40/FSharpx.Extras.dll"
#r "packages/FSharpX.Collections/lib/net40/FSharpx.Collections.dll"
#r "System.Net.Http.dll"

#load "common.fsx"

module Eventbrite =
  open Common
  open System
  open System.Net
  open Suave.Successful
  open FSharp.Data
  open System.Net.Http

  let httpClient = new HttpClient() 
  System.Net.ServicePointManager.SecurityProtocol <- SecurityProtocolType.Tls12

  let [<Literal>] eventsSample = """
    { 
      "config" : {
        "isWinter": false, 
        "isSummer": false
      },
      "events" : [
      { "title":"talk title", 
        "date":"2011-01-01T17:00:00",
        "id" : "use as string",
        "description" : "string",
        "enddate" : "2011-01-01T17:30:00",
        "status" : "string",
        "logo"   : "url string",
        "link"   : "http://site/link_to_this_event",
        "venue"  : {"name":"string", "address":"123 mean st.", "id":"use as string"}
      }
    ]}
  """

  type EventsJson = JsonProvider<eventsSample, RootName="root">

  type EbEventsJson = JsonProvider<""" {"pagination": {"object_count": 32, "page_number": 1, "page_size": 50, "page_count": 1}, 
                                              "events": [{"name": {"text": "T vs B", "html": "T vs B"}, 
                                                          "description": {"text": "description in text format", "html": "description in html"}, 
                                                          "id": "could be a string",
                                                          "status": "draft",
                                                          "url": "http://www.eventbrite.ca/e/blah", 
                                                          "start": {"timezone": "America/Winnipeg", "local": "2016-04-21T17:30:00", "utc": "2016-04-21T22:30:00Z"}, 
                                                          "end": {"timezone": "America/Winnipeg", "local": "2016-04-21T20:30:00", "utc": "2016-04-22T01:30:00Z"}, 
                                                          "logo": { "url" : "some link" },
                                                          "venue": {"address": {"address_1": "251 Donald Street"}, "id": "stringify", "name": "Millenium Library   Buchwald Room, 2nd floor"}},
                                                          {"name": {"text": "T vs B", "html": "T vs B"}, 
                                                          "description": {"text": "description in text format", "html": "description in html"}, 
                                                          "id": "could be a string",
                                                          "status": "draft",
                                                          "url": "blah", 
                                                          "start": {"timezone": "America/Winnipeg", "local": "2016-04-21T17:30:00", "utc": "2016-04-21T22:30:00Z"}, 
                                                          "end": {"timezone": "America/Winnipeg", "local": "2016-04-21T20:30:00", "utc": "2016-04-22T01:30:00Z"}, 
                                                          "logo": null,
                                                          "venue": {"address": {"address_1": "asdf"}, "id": "stringify", "name": "Millenium Library   Buchwald Room, 2nd floor"}}]} """>

  let createEvent (e : EbEventsJson.Event) = 
    EventsJson.Event(
      title   = e.Name.Text, 
      status  = e.Status,
      date    = e.Start.Utc,
      enddate = e.End.Utc,
      id      = e.Id,
      link    = e.Url,
      logo    = (e.Logo |> Option.map (fun l -> l.Url) |> FSharpx.Option.getOrElse ""), 
      description = e.Description.Text,
      venue = EventsJson.Venue(name=e.Venue.Name,address=e.Venue.Address.Address1,id=e.Venue.Id))

  let getEvents ctx =
    let onlyPublished (e: EbEventsJson.Event) = e.Status <> "draft"
    let eventDate (e: EventsJson.Event) = e.Date

    let isJustBefore (d1: DateTime) (d2: DateTime) = 
      let target = d2.AddMonths(-1)
      d1.Month = target.Month && d1.Year = target.Year

    async {
      httpClient.DefaultRequestHeaders.Authorization <- new Headers.AuthenticationHeaderValue("Bearer", "EB_AUTH_TOKEN" |> Env.getVar)
      let! resp = httpClient.GetAsync("https://www.eventbriteapi.com/v3/users/me/owned_events/?order_by=start_desc&expand=venue")
                    |> Async.AwaitTask
      let! contents = resp.Content.ReadAsStringAsync () |> Async.AwaitTask 
      httpClient.DefaultRequestHeaders.Authorization <- null
      let published = EbEventsJson.Parse(contents).Events |> Array.filter onlyPublished |> Array.map createEvent
      let lastEvent = published |> Array.head |> eventDate
      let now = DateTime.UtcNow 
      let alreadyHappened  = lastEvent < now 

      let isSummer = Season.isSummer now || (alreadyHappened && Season.isBeforeSummer lastEvent)

      let isWinter = Season.isWinter now && alreadyHappened

      let config = EventsJson.Config(isSummer = isSummer, isWinter = isWinter)

      return! EventsJson.Root(config, published) |> jsonStr |> flip OK ctx
    }
