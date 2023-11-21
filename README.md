# AdGoBye - Defusing Advertisements in Virtual Reality

## Rationale

Over the years, advertisements have slowly snuck their way into worlds at a pace that we haven't ever seen
before. Creators of worlds are handed silly sums of money. This would be fine, advertisements however, slowly reached an
excessive amount.

![img.png](Marketing%2Fimg.png)
No offense to the creator of Movie & Chill, but almost every wall has at least one advertisement.

## Architecture

AGB works by modifying AssetBundles in the Cache folder, the GameObjects in Worlds have known names which remain
mostly static, therefore, we can either remove or disable them.

AGB has two methods for this: Blocklists and Plugins

### Blocklist

We want to give users the choice of what to hide by borrowing a mechanism similar to uBlock Origin's Filter lists.

Blocklists contain the World IDs (`wrld_*`) with the names of GameObjects to disable, AGB then disables these
objects.

We will provide first-party blocklists (which are entirely opinionated from the AGB authors), but we allow users to
load their own blocklists instead.

### Plugins

Certain worlds require more work than just disabling GameObjects (texture swaps, removing String / Image loaders), which
might require specialized code.
Because of this, Plugins allow users to load custom code to modify worlds.

While we internally use [AssetTools.net](https://github.com/nesrak1/AssetsTools.NET), plugins are able to use their own
code.

## Ethics

AGB is developed out of love and out of fear.
Worlds are special,
they are places you develop some of your fondest memories in and push the boundaries of the technology we have today.

But over the recent months, there has been an obvious culture shift.
We've observed the ads getting more and more obtrusive.

The argument that "you should just go to a world that doesn't have ads"
is not compatible with reality; worlds get updated which might introduce ads.

There is seemingly no end to this wave of ads. In fact, their proliferation seems to imply that they're successful.
Names like Flirtual and Nevermet get away with no repercussions for preying on the socially awkward.
![Two advertisements for Flirtual and Nevermet that market themselves for making friends](Marketing/datingads.png)

During the EAC mod purge, it was justified that external tooling like mods impede the vision of world creators.
While that is valid to a degree, the question is where this ends:

![](Marketing/rainyrooftop_nevermet.webm)

<Sub>this guy must have startled everybody at least once</sub>

While these ads may fund the creator, they come at the determent of the users who actually inhabit the world.

AdGoBye allows those who are interested in taking back their virtual reality real estate to do so.

<br/><br/>

If you're a world creator, and you have ads that AGB blocks,
you are likely being paid a flat rate for the ads existing in your world.

Please understand AGB is the best compromise in this system, it doesn't matter if (slightly) fewer people see the ads
because you will get paid either way.

The difference is that users like me, who are overstimulated by ads,
who don't have the expendable income to spend on the products shown,
who have no interest in the products will have a better experience in your world.

Forcing advertisements onto the people who don't want them will sour their experience with the world
you have spent your hard time working on, it's not for the mutual benefit for both of us.

And if a user wants to see your world as-is, they can use the Allowlist feature provided which will skip your world
based on ID from being indexed.

But for the love of god, please oppose actors like [Adlily](https://adli.ly) (fyi, website has tracking) who are
seeking to profit from this community we've built.
That use new tools provided by the game, not the novel uses that push this medium forward but drag us back to the
current Internet dark age
by [spying and tracking users](https://web.archive.org/web/20231120221251/https://adli.ly/analytics/retention).
All for meaningless statistics like "ad retention"
or ["impressions"](https://web.archive.org/web/20231015041525/https://adli.ly/faq#whats-an-impression)
that disregard the humanity of you and your users.

These people are not your friends;
they are entrepreneurs exploiting your creativity and goodwill as a vehicle to make money off you.
By turning your work into a billboard.

<br/><br/>
You deserve better than that.