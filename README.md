# BlooLynx

An unofficial `netstandard` C# library that interrogates the US Hyundai API to control your car.

> Use at your own risk. This is totally unofficial, and it's provided as-is, with no guarantees about the suitability of any use or purpose. You've been warned.

<img width="500" height="936" alt="Screenshot_20260725-172650" src="https://github.com/user-attachments/assets/9764604b-6c9b-4ce3-a76c-959a3a418708" />

## Getting started

Check out the `apireference.md` in the `docs` folder for information on the API itself. Hyundai's overall design is a little clunky, because it doesn't stream data from the car to the client. It's all one-off calls and long-polling to get a result. It's also different in every region, so I'm not sure how they maintain all of these odd branches.

The project also contains an Android app, which you can side load with the directions below, at your own risk.

## Why does this even exist?

It's partly a science project, but also I just don't care for the official app. It has so much junk in it that's not useful or noisy. Less is more, and that was a goal here. I also don't like the notifications and toasts and whatnot that exist only to reflect the non-streaming async nature of the architecture. So that's why BlooLynx exists.

A big add from the second beta was to accept the user's input for battery size and average efficiency to show an estimated range, instead of the crap one sent by the car. I'll never understand why they base the range on the last bit you drove, which is inevitably slowly on a residential street.

The original intent was to build it as a web app, so there'd be nothing to install at all, but the API doesn't play nice with CORS policies in the browser, so it's not possible. I'm a huge proponent of web apps (see [MLocker](https://github.com/POPWorldMedia/MLocker)).

## To use the Android app

This won't live in an app store, because it's unofficial and a hobby project. Fortunately, you can download apps and install them yourself. Again, do this at your own risk.
* Open **Settings**
* Tap **Apps**
* Tap **Special app access**
* Tap **Install unknown apps**
* Tap **Chrome**
* Download the `.apk` file from [the releases section on GitHub](https://github.com/POPWorldMedia/BlooLynx/releases) in Chrome
* Tap the three dots in Chrome, then select **Downloads**
* Select the file and tap **Install**
* It will warn you, but drill into the drop down and install anyway

## Android app features

* Current battery/fuel percentage and range
* Lock/Unlock
* Climate start/stop with temperature setting
* Charge start/stop (EVs)
* Set charge limits (EVs)
* Show remaining charge time and power (EVs)
* Display estimated range based on user input
* Tire pressure
* Flash lights
* Flash lights and honk
* Display odometer and VIN
* Display year/make/model
* Dark mode
