# Google Business Profile (GBP) - capabilities research

Researched 2026-11-16 from official pages only (developers.google.com, support.google.com, cloud.google.com).
Legend: CONFIRMED = stated in an official page I opened (quote + URL). UNVERIFIED = docs silent, conflicting, or not opened. BLOCKED = unsupported, closed, or needs an approval we do not have.
Page "Last updated" stamps are quoted where they matter, so staleness can be judged.

Short URL keys used below (full list at the end):
- REF = https://developers.google.com/my-business/reference/rest
- SUNSET = https://developers.google.com/my-business/content/sunset-dates
- PREREQ = https://developers.google.com/my-business/content/prereqs
- LIMITS = https://developers.google.com/my-business/content/limits
- POLICY = https://developers.google.com/my-business/content/policies
- NOTSET = https://developers.google.com/my-business/content/notification-setup
- NOTREF = https://developers.google.com/my-business/reference/notifications/rest/v1/NotificationSetting
- V4REV = https://developers.google.com/my-business/reference/rest/v4/accounts.locations.reviews
- V4LP = https://developers.google.com/my-business/reference/rest/v4/accounts.locations.localPosts
- V4MEDIA = https://developers.google.com/my-business/reference/rest/v4/accounts.locations.media

---

## 1. APIs and versions

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Account Management API is v1 | `https://mybusinessaccountmanagement.googleapis.com` (v1) | business.manage | CONFIRMED | "provides an interface for managing access to a location on Google." https://developers.google.com/my-business/reference/accountmanagement/rest (discovery `?version=v1`) |
| Business Information API is v1 (also a v1alpha) | `https://mybusinessbusinessinformation.googleapis.com` (v1, v1alpha) | business.manage | CONFIRMED | "provides an interface for managing business information on Google." https://developers.google.com/my-business/reference/businessinformation/rest |
| Notifications API is v1 (separate service) | `https://mybusinessnotifications.googleapis.com/v1/...` | business.manage | CONFIRMED | "accessible at https://mybusinessnotifications.googleapis.com/v1/accounts/{accountId}/notificationSetting instead of https://mybusiness.googleapis.com/v4/accounts/{accountId}/notifications" https://developers.google.com/my-business/content/notifications/change-log |
| Reviews live ONLY on the v4 "Google My Business API" | `https://mybusiness.googleapis.com/v4/...` | business.manage (plus.business.manage deprecated alias) | CONFIRMED | Service page lists v4 resources incl. `accounts.locations.reviews`, `accounts.locations.localPosts`, `accounts.locations.media`; v1 there is "media (upload only)". https://developers.google.com/my-business/reference/rest |
| Reviews API still supported (not on the deprecation schedule) | v4 `accounts.locations.reviews` | - | CONFIRMED (by absence plus recent changes) | Deprecation schedule lists only Performance metrics, Calls API, associate/clearAssociation, Insurance/HealthProvider, Q&A. Reviews gained fields 2026-04-01, 2026-04-20, 2026-07-01, 2026-07-24: "The reviewReplyUrl can now be retrieved as part of reviews". https://developers.google.com/my-business/content/sunset-dates and https://developers.google.com/my-business/content/latest-updates |
| Local Posts API still supported | v4 `accounts.locations.localPosts` | - | CONFIRMED (by absence plus recent changes) | "You can now schedule recurring posts by setting RecurrenceInfo when creating a LocalPost." (2026-04-07). https://developers.google.com/my-business/content/latest-updates |
| v4 methods that ARE marked deprecated | v4 `accounts.*` (create, get, list, getNotifications, updateNotifications, deleteNotifications...), `accounts.admins.*`, `accounts.invitations.*`, `accounts.locations.transfer`, `accounts.locations.admins.*` | - | CONFIRMED | Method lists carry the "(deprecated)" tag, e.g. "getNotifications (deprecated)", "list (deprecated)". https://developers.google.com/my-business/reference/rest/v4/accounts. Replacement is the v1 Account Management API (page notes no explicit "use X" line; the v1 resources mirror them). |
| v4 `accounts.locations.list/get/patch` (location read/write) | v4 | - | UNVERIFIED | NOT tagged deprecated on https://developers.google.com/my-business/reference/rest/v4/accounts.locations, but Business Information v1 is the documented location API (https://developers.google.com/my-business/content/location-data). Use v1; I did not find an official "v4 locations deprecated" statement. |
| Q&A API | `accounts.locations.questions*` | - | BLOCKED (discontinued) | "The My Business Q&A API was discontinued on November 3, 2025." https://developers.google.com/my-business/content/sunset-dates |
| Business Profile Performance API (insights) | `businessprofileperformance` v1 | - | CONFIRMED (exists) | v4 `reportInsights` replaced by `locations.fetchMultiDailyMetricsTimeSeries` per https://developers.google.com/my-business/content/sunset-dates |
| Version-support wording | - | - | CONFIRMED | "Support Ended indicates that the version of the API will continue to function as expected, but may not be updated with new features or bug fixes." https://developers.google.com/my-business/content/sunset-dates |
| Pricing | all | - | CONFIRMED | "The Google My Business API is available to registered users at no charge." https://developers.google.com/my-business/content/pricing |
| APIs that must be enabled in the Cloud project | Google My Business API, Account Management, Lodging, Place Actions, Notifications, Verifications, Business Information | - | CONFIRMED (note count mismatch) | Page lists seven APIs; its own summary line says "eight". Enable-list quote: "There are seven APIs associated with Business Profile that must be enabled". https://developers.google.com/my-business/content/basic-setup |
| No sandbox | - | - | CONFIRMED | "There's no Sandbox environment for the Business Profile APIs." https://developers.google.com/my-business/content/basic-setup |

---

## 2. Listing accounts and locations (customer picks at connect time)

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| List accounts the signed-in user can manage | `GET https://mybusinessaccountmanagement.googleapis.com/v1/accounts` (params: `parentAccount`, `pageSize` default+max 20, `pageToken`, `filter` by `type`) | `https://www.googleapis.com/auth/business.manage` | CONFIRMED | "This includes all accounts that the user owns, as well as any accounts for which the user has management rights." https://developers.google.com/my-business/reference/accountmanagement/rest/v1/accounts/list |
| Personal account is first in list | same | same | CONFIRMED | "The personal account of the user doing the query will always be the first item of the result, unless it is filtered out." same URL |
| Account fields useful at connect | `Account`: `name` (accounts/{id}), `accountName`, `type` (PERSONAL, LOCATION_GROUP, USER_GROUP, ORGANIZATION), `role`, `verificationState`, `vettedState`, `permissionLevel` | - | CONFIRMED | "Immutable. The resource name, in the format accounts/{account_id}." https://developers.google.com/my-business/reference/accountmanagement/rest/v1/accounts |
| List locations in an account | `GET https://mybusinessbusinessinformation.googleapis.com/v1/{parent=accounts/*}/locations` (REQUIRED `readMask`; `pageSize` default 10, max 100; `filter`; `orderBy` title/storeCode) | `https://www.googleapis.com/auth/business.manage` | CONFIRMED | "Wildcard ( - ) : Using accounts/- identifies the authenticated user." and "Minimum is 1, and maximum page size is 100." https://developers.google.com/my-business/reference/businessinformation/rest/v1/accounts.locations/list |
| Personal vs group account behaviour | same | same | CONFIRMED | "If the account is of type AccountType.PERSONAL, the response returns only locations directly owned by that account." same URL |
| Is the customer's location verified / in good standing (Voice of Merchant) | `GET https://mybusinessverifications.googleapis.com/v1/{name=locations/*}/VoiceOfMerchantState` | business.manage | CONFIRMED | "Indicates whether the location is in good standing and has control over the business on Google." https://developers.google.com/my-business/reference/verifications/rest/v1/locations/getVoiceOfMerchantState |
| Older equivalent (v4) | `GET https://mybusiness.googleapis.com/v4/{parent=accounts/*}/locations` (pageSize max 100) | business.manage / plus.business.manage | CONFIRMED (exists) | "maximum page size is 100." https://developers.google.com/my-business/reference/rest/v4/accounts.locations/list |
| Review/post endpoints need BOTH accountId and locationId in the path | `accounts/{accountId}/locations/{locationId}/...` | - | CONFIRMED | Review name form: "accounts/{accountId}/locations/{locationId}/reviews/{reviewId}". https://developers.google.com/my-business/reference/rest/v4/accounts.locations.reviews |

---

## 3. Reading and replying to reviews

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| List reviews of one location | `GET https://mybusiness.googleapis.com/v4/{parent=accounts/*/locations/*}/reviews` | business.manage or plus.business.manage | CONFIRMED | "The maximum `pageSize` is 50." / orderBy: "Valid orders to sort by are `rating`, `rating desc` and `updateTime desc`." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.reviews/list (default order `updateTime desc`; response has `averageRating`, `totalReviewCount`, `nextPageToken`) |
| Get one review | `GET .../v4/accounts/{a}/locations/{l}/reviews/{r}` | same | CONFIRMED | "accounts.locations.reviews.get doesn't currently return the reviewReplyState or policyViolation fields of a review reply." https://developers.google.com/my-business/content/review-data |
| Reviews across many locations | `POST https://mybusiness.googleapis.com/v4/{name=accounts/*}/locations:batchGetReviews` | same | CONFIRMED | "Reviews are limited to a batch size of 50 locationNames per call." https://developers.google.com/my-business/reference/rest/v4/accounts.locations/batchGetReviews |
| Reviews are only returned for verified locations | list / batchGetReviews | - | CONFIRMED | "This operation is only valid if the specified locations are verified." (same batchGetReviews URL); the v4 list page also says verified-only, but I read that sentence only through a summarizing fetch, not in the raw text, so treat the list case as the same rule, unquoted. Reply quote is below. |
| Create/update a reply | `PUT https://mybusiness.googleapis.com/v4/{name=accounts/*/locations/*/reviews/*}/reply` body `{"comment":"..."}` | business.manage or plus.business.manage | CONFIRMED | "Updates the reply to the specified review. A reply is created if one does not exist. This operation is only valid if the specified location is verified." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.reviews/updateReply |
| Delete a reply | `DELETE https://mybusiness.googleapis.com/v4/{name=accounts/*/locations/*/reviews/*}/reply` | same | CONFIRMED | Endpoint and scopes on https://developers.google.com/my-business/reference/rest/v4/accounts.locations.reviews/deleteReply (page gives no further text) |
| Reply length | ReviewReply.comment | - | CONFIRMED | "The body of the reply as plain text with markups. The maximum length is 4096 bytes." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.reviews |
| Workspace-admin precondition for reply | - | - | CONFIRMED | "your G Suite Organization Administrator has enabled Google Search and/or Google Maps as services for your account, along with Business Profile" https://developers.google.com/my-business/content/review-data |
| Review field: reviewer name | `reviewer.displayName`, `reviewer.isAnonymous`, `reviewer.profilePhotoUrl` | - | CONFIRMED | "Only populated with the reviewer's real name if isAnonymous is false." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.reviews |
| Review field: rating | `starRating` enum ONE..FIVE (not an integer) | - | CONFIRMED | "The star rating out of five, where five is the highest rated." same URL |
| Review field: comment | `comment` | - | CONFIRMED | "The body of the review as plain text with markups." same URL. A rating-only review has no comment (list param `ignoreRatingOnlyReviews` exists on batchGetReviews but is tagged deprecated). |
| Review field: timestamps | `createTime`, `updateTime` (RFC 3339) | - | CONFIRMED | "The timestamp for when the review was written." / "...last modified." same URL |
| Review field: ids | `name`, `reviewId` ("The encrypted unique identifier") | - | CONFIRMED | same URL |
| Other review fields | `reviewReply{comment, updateTime, reviewReplyState PENDING/REJECTED/APPROVED, policyViolation}`, `reviewMediaItems[]`, `reviewReplyUrl` | - | CONFIRMED | "REVIEW_REPLY_STATE ... PENDING Indicates that the reply is pending moderation." same URL |
| Replies are moderated before going public | UI/help | - | CONFIRMED | "Replies usually take up to 10 minutes to review, but sometimes a review can take up to 30 days." https://support.google.com/business/answer/3474050 . So an API 200 is not "published". |
| Reply requires a verified business | help | - | CONFIRMED | "Before you can reply to reviews, you must verify your business." https://support.google.com/business/answer/3474050 |
| Reviews availability limits | help | - | CONFIRMED | "Reviews are available only in certain countries or regions and for certain business categories." same URL |

---

## 4. Local Posts

Create endpoint: `POST https://mybusiness.googleapis.com/v4/{parent=accounts/*/locations/*}/localPosts`, scope business.manage or plus.business.manage. CONFIRMED: "Creates a new local post associated with the specified location, and returns it." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.localPosts/create

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Topic types | `topicType` (REQUIRED): STANDARD, EVENT, OFFER, ALERT | business.manage | CONFIRMED | "Required. The topic type of the post: standard, event, offer, or alert." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.localPosts |
| STANDARD | summary + media + optional CTA | - | CONFIRMED | "Post contains basic information, like summary and images." same URL |
| EVENT | needs `event{title, schedule{startDate,startTime,endDate,endTime}}` | - | CONFIRMED | "Event information. Required for topic types EVENT and OFFER." and "It must contain all fields to be valid." same URL |
| OFFER | `event` required + `offer{couponCode, redeemOnlineUrl, termsConditions}` (all optional); `callToAction` ignored | - | CONFIRMED | "Ignored for topic type OFFER." same URL |
| ALERT | `alertType` (only COVID_19 defined) | - | CONFIRMED / effectively closed | "These types of posts are not always available for authoring." and for COVID_19 "new Alert post creation for type COVID-19 will be disabled." same URL. Treat ALERT as not usable. |
| Body text | `summary` ("Description/body of the local post.") `languageCode` | - | CONFIRMED | same URL |
| Summary character limit | - | - | UNVERIFIED | No limit stated on the reference page or the help page I read. Needs a live call. |
| Call-to-action | `callToAction{actionType, url}`; actionType BOOK, ORDER, SHOP, LEARN_MORE, SIGN_UP, CALL (GET_OFFER deprecated) | - | CONFIRMED | "The URL the user will be directed to upon clicking. This field should be left unset for Call CTA." same URL |
| Link / article share | No dedicated link-share or article-card post type. The only link slot is `callToAction.url` (e.g. LEARN_MORE); OFFER has `redeemOnlineUrl`. A link-preview/card is NOT documented. | - | CONFIRMED (absence) / link preview UNVERIFIED | Help page: "an action button with a link. The button should direct customers to a website where they can complete the action" https://support.google.com/business/answer/7342169 |
| Scheduling | `scheduledTime` ("If set, determines when a post will be published.") and `event.recurrenceInfo` (daily/weekly/monthly) | - | CONFIRMED | same reference URL; states SCHEDULED and RECURRING exist |
| Post states | LIVE, PROCESSING, REJECTED, SCHEDULED, RECURRING | - | CONFIRMED | "REJECTED This post was rejected due to content policy violation." same URL |
| Share URL of a published post | `searchUrl` (output only) | - | CONFIRMED | "This link can be used to share the post via social media, email, text, etc." same URL |
| Other post methods | `get`, `list`, `patch`, `delete` | - | CONFIRMED | Method list on same URL. `reportInsights` was removed (see SUNSET). |
| Post review by Google / unverified profile | help | - | CONFIRMED | "Pending: The post doesn't show on Search or Maps. It's either in the upload stage, the processing stage, or your profile is unverified." https://support.google.com/business/answer/7342169 |
| Post content rules | help | - | CONFIRMED | "Do not include content about the products." for regulated goods; phone numbers: "might get rejected if they cannot be verified as being connected to the business" https://support.google.com/business/answer/7342169 |
| Posts older than 6 months | help | - | CONFIRMED | "Posts older than 6 months are archived unless a date range is set." same URL |

---

## 5. MEDIA RULES

Plain answer:
- **Local post images: URL only.** The LocalPost `media[]` field documents `sourceUrl` as the only supported field, so no binary upload and no `dataRef` for posts. The URL must be publicly accessible (see the quote for `sourceUrl` below).
- **Location photos (profile, cover, exterior, etc.): either a public `sourceUrl` OR binary upload** via `media:startUpload`, then `POST /upload/v1/media/{resourceName}?uploadType=media`, then `media.create` with `dataRef`.

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Image on a local post | `LocalPost.media[]` (MediaItem with `sourceUrl`) | business.manage | CONFIRMED | "sourceUrl is the only supported data field for a LocalPost MediaItem." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.localPosts |
| URL must be public | `MediaItem.sourceUrl` | - | CONFIRMED | "A publicly accessible URL where the media item can be retrieved from." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.media |
| Location media, URL path | `POST https://mybusiness.googleapis.com/v4/{parent=accounts/*/locations/*}/media` with `mediaFormat: PHOTO`, `locationAssociation.category`, `sourceUrl` | business.manage | CONFIRMED | "When creating a new media item, either a URL or data ref must be provided." V4MEDIA |
| Location media, binary path step 1 | `POST https://mybusiness.googleapis.com/v4/{parent=accounts/*/locations/*}/media:startUpload` (empty body) returns `MediaItemDataRef{resourceName}` | business.manage | CONFIRMED | "The request body must be empty." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.media/startUpload |
| Binary path step 2 | `POST https://mybusiness.googleapis.com/upload/v1/media/{resourceName}?uploadType=media` | business.manage | CONFIRMED | `curl -X POST -T{path_to_file} "http://mybusiness.googleapis.com/upload/v1/media/{resourceName}?uploadType=media"` (V4MEDIA); upload method page: "Upload is supported on the URI /upload/v1/media/{+name}." https://developers.google.com/my-business/reference/media/rest/v1/media/upload |
| Binary path step 3 | `media.create` with `dataRef` | business.manage | CONFIRMED | "For media.create calls, set this as the MediaItem dataRef." V4MEDIA |
| Accepted formats (photo) | JPG, PNG | - | CONFIRMED | "Format: JPG or PNG." https://support.google.com/business/answer/6103862 (UI help page; the API doc says photos "should follow the Business Profile guidelines for photos") |
| File size (photo) | 10 KB to 5 MB | - | CONFIRMED | "Size: Between 10 KB and 5 MB." https://support.google.com/business/answer/6103862 . API doc: "a file size of at least 10240 bytes" (V4MEDIA) |
| Pixel minimum | 250 px on short edge (API); 250x250 (help) | - | CONFIRMED | "all photos must measure a minimum of 250px on the short edge, with a file size of at least 10240 bytes." V4MEDIA (exception: PROFILE and COVER categories) |
| Pixel recommended | 720 x 720 | - | CONFIRMED | "Recommended resolution: 720 px tall, 720 px wide." https://support.google.com/business/answer/6103862 |
| Pixel maximum | - | - | UNVERIFIED | No maximum dimension stated on any page I read. |
| Aspect ratio | - | - | UNVERIFIED | No aspect ratio rule found in the API reference or the photo help page. |
| Post-specific image size/format limits | - | - | UNVERIFIED | Nothing post-specific found; assume the photo rules above, but untested. |
| Video | `mediaFormat: VIDEO` exists in enum; help says up to 30 s, 75 MB, 720p+ | - | CONFIRMED (help) / API support UNVERIFIED | Enum text says "PHOTO Media item is a photo. In this version, only photos are supported." (V4MEDIA) - conflicts with help page "Duration: Up to 30 seconds long File size: Up to 75 MB". Treat video via API as UNVERIFIED. |
| Photo may not appear until reviewed/verified | help | - | CONFIRMED | "It can take up to 24-48 hours before your photos and videos show on your Business Profile." and "Your photos and videos show up on Google only after you verify your Business Profile." https://support.google.com/business/answer/6103862 |
| Profile/logo quirk | `category` PROFILE/LOGO | - | CONFIRMED | "The original sourceUrl provided in the request is not retained in the response." https://developers.google.com/my-business/reference/rest/v4/accounts.locations.media/create |
| Media descriptions | - | - | CONFIRMED | "Descriptions cannot be modified through the Google My Business API, but can be set when creating a new media item" V4MEDIA |

---

## 6. NEW-REVIEW DELIVERY (Pub/Sub)

How it is set up (CONFIRMED, https://developers.google.com/my-business/content/notification-setup):
1. Create a Cloud Pub/Sub topic in our project.
2. Grant publish: "Give at least pubsub.topics.publish permissions to mybusiness-api-pubsub@system.gserviceaccount.com."
3. Create a push or pull subscription: "Follow the Subscriber overview guide to set up either push or pull notifications."
4. Per customer account: `PATCH https://mybusinessnotifications.googleapis.com/v1/accounts/{accountId}/notificationSetting?updateMask=...` with `pubsubTopic` (and `notificationTypes`). "(Optional) Repeat step 5 for each Business Profile account that you want to receive notifications for."

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Set a customer account's notification setting | `PATCH https://mybusinessnotifications.googleapis.com/v1/{notificationSetting.name=accounts/*/notificationSetting}` (required `updateMask`) | `https://www.googleapis.com/auth/business.manage` | CONFIRMED | "An account will only have one notification setting resource, and only one pubsub topic can be set." https://developers.google.com/my-business/reference/notifications/rest/v1/accounts/updateNotificationSetting |
| Read the setting | `GET https://mybusinessnotifications.googleapis.com/v1/{name=accounts/*/notificationSetting}` | business.manage | CONFIRMED | "Returns the pubsub notification settings for the account." https://developers.google.com/my-business/reference/notifications/rest |
| Remove the setting | PATCH with empty `pubsubTopic` | business.manage | CONFIRMED | "Calling the accounts.updateNotificationSetting with an empty pubsubTopic deletes the Cloud Pub/Sub notification settings from an account." https://developers.google.com/my-business/content/notification-setup |
| Publisher service account | `mybusiness-api-pubsub@system.gserviceaccount.com` | needs Publish on our topic | CONFIRMED | "must have at least Publish permissions on the Pub/Sub topic." https://developers.google.com/my-business/reference/notifications/rest/v1/NotificationSetting |
| Fields | `name`, `pubsubTopic`, `notificationTypes[]` | - | CONFIRMED | "If unset, no notifications will be posted." same NotificationSetting URL. Topic is a full name `projects/{p}/topics/{t}` (page shows `your/pubsub/topicName`). |
| Notification type NEW_REVIEW | enum | - | CONFIRMED | "A new review has been added to the location." / "The review_name field on the notification will provide the resource name of the review that was added" NOTREF |
| UPDATED_REVIEW | enum | - | CONFIRMED | "A review on the location has been updated." NOTREF. Whether a reply by the owner or only the reviewer's edit triggers this: UNVERIFIED. |
| GOOGLE_UPDATE | enum | - | CONFIRMED | "The location has Google updates for review." NOTREF |
| NEW_CUSTOMER_MEDIA | enum | - | CONFIRMED | "A new media item has been added to the location by a Google Maps user." NOTREF |
| DUPLICATE_LOCATION | enum | - | CONFIRMED | "Indicates whether there is a change in location metadata's duplicate location field." NOTREF |
| VOICE_OF_MERCHANT_UPDATED | enum | - | CONFIRMED | "Indicates whether the location has an update in Voice of Merchant (VOM) status." NOTREF |
| LOSS_OF_VOICE_OF_MERCHANT | enum | - | CONFIRMED (deprecated) | "Migrate the existing usages of this value to the more expanded "VOICE_OF_MERCHANT_UPDATED"." NOTREF |
| NEW_QUESTION / UPDATED_QUESTION / NEW_ANSWER / UPDATED_ANSWER | enum | - | BLOCKED | "The My Business Q&A API was discontinued on November 3, 2025." NOTREF and https://developers.google.com/my-business/content/notifications/change-log |
| Payload content: review text? | Pub/Sub message `data` | - | CONFIRMED that it carries resource names; exact JSON UNVERIFIED | The docs describe only name fields: "The review_name field on the notification will provide the resource name of the review that was added, and location_name will have the location's resource name." NOTREF. No page I opened shows a sample message, shows the review text/rating in it, or documents message attributes. Plan on: notification = trigger, then `reviews.get` for text. Exact field spellings and encoding of `data` need a live message. |
| Delivery guarantee / latency of Google's publish | - | - | UNVERIFIED | GBP docs state no latency or delivery SLA. Only the usage page says Pub/Sub keeps data "updated in real time without polling the API." (https://developers.google.com/my-business/content/limits) - this is a marketing statement, not a latency number. |
| Pub/Sub's own guarantee | cloud.google.com | - | CONFIRMED | "By default, Pub/Sub offers at-least-once delivery with no ordering guarantees on all subscription types." https://cloud.google.com/pubsub/docs/subscription-overview . So duplicates and reordering must be handled (dedupe on review name + updateTime). |
| Retention if our endpoint is down | Pub/Sub subscription | - | CONFIRMED | "Default value = 7 days Minimum value = 10 minutes Maximum value = 31 days" (message retention duration) https://cloud.google.com/pubsub/docs/subscription-properties |
| Push to an HTTPS endpoint is possible | Pub/Sub push subscription | - | CONFIRMED | "Endpoint URL (required). A publicly accessible HTTPS address. The server for the push endpoint must have a valid SSL certificate signed by a certificate authority." https://cloud.google.com/pubsub/docs/create-push-subscription |
| Push mechanics and ack | POST wrapped JSON | - | CONFIRMED | "The endpoint acknowledges the message by returning an HTTP success status code. A non-success response indicates that Pub/Sub must resend the messages." https://cloud.google.com/pubsub/docs/push ; body: "The message data is in the message.data field and is base64-encoded." |
| Push authentication | OIDC JWT in `Authorization: Bearer` | our service account with `iam.serviceAccountTokenCreator` granted to the Pub/Sub service agent | CONFIRMED | "the Pub/Sub service signs a JSON Web Token (JWT) and sends the JWT in the authorization header of the push request." https://cloud.google.com/pubsub/docs/authenticate-push-subscriptions ; claims include `aud`, `email`, `iss: https://accounts.google.com`; "The tokens attached to requests sent to push endpoints may be up to an hour old." Verify signature, `aud`, and `email` on every call. |
| Push firewall caveat | - | - | CONFIRMED | "If subscribers use a firewall, they can't receive push requests." same authenticate-push URL |
| On-demand alternative (no timer) | `reviews.list` / `batchGetReviews` / `reviews.get` invoked by a person's action (e.g. "Sync now") | business.manage | CONFIRMED (endpoints exist) | Endpoints in section 3. Quota: 300 QPM default (section 8). The usage page tells developers to prefer push: "Subscribe to Pub/Sub notifications to keep your databases updated in real time without polling the API." LIMITS |
| Does the notification fire for non-verified locations | - | - | UNVERIFIED | Not stated. Reviews APIs need verified locations. |
| Who may set the notification setting (owner vs manager) | - | - | UNVERIFIED | Not stated; needs a live call as a manager-level user. |
| Quota on Notifications API | - | - | CONFIRMED | "Notifications API 300 QPM" LIMITS |

---

## 7. OAuth

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Scope | authorization via Google OAuth 2.0 | `https://www.googleapis.com/auth/business.manage` | CONFIRMED | "Manage your Business Profile on Google" https://developers.google.com/identity/protocols/oauth2/scopes (Google Business Profile API row). One scope covers read and write; no read-only scope is listed. |
| Deprecated alias | `plus.business.manage` | - | CONFIRMED | "The plus.business.manage scope has been deprecated and is available to maintain backward compatibility for existing implementations." https://developers.google.com/my-business/content/implement-oauth |
| Offline access / refresh token | `access_type=offline` | - | CONFIRMED | "Enables partner platforms to perform online or offline actions on behalf of the business owner. This includes responses to reviews, creation of posts" https://developers.google.com/my-business/content/implement-oauth ; "the refresh token is only returned if your application set the access_type parameter to offline" https://developers.google.com/identity/protocols/oauth2/web-server |
| Refresh token lifetime | - | - | CONFIRMED | "Refresh tokens are valid until the user revokes access or the refresh token expires." web-server URL above |
| Refresh token can stop working | - | - | CONFIRMED | "The refresh token has not been used for six months." plus: revoked, admin set service Restricted, over the limit. https://developers.google.com/identity/protocols/oauth2 |
| Refresh token cap | - | - | CONFIRMED | "There is currently a limit of 100 refresh tokens per Google Account per OAuth 2.0 client ID." same URL (oldest is silently invalidated) |
| Consent screen in "Testing" status | - | - | CONFIRMED | "a publishing status of "Testing" is issued a refresh token expiring in 7 days" same URL. We must publish ("In production") for lasting tokens. |
| Access token lifetime | - | - | UNVERIFIED (exact value) | Docs say "short-lived access token" and return `expires_in`; the only sample value on the pages I read is 3920 (a sample, not a rule). Do not hardcode 3600; read `expires_in`. |
| Admin policy block | - | - | CONFIRMED | "If an admin set any of the services requested in your app's scopes to Restricted (the error is admin_policy_enforced)." OAuth overview URL. Also Workspace: "You will get `error 403 - PERMISSION DENIED` when using the GBP APIs if Google Business Profile is turned off for your account in your Google workspace organization." https://developers.google.com/my-business/content/basic-setup |
| PKCE | `code_challenge` (S256 or plain) | - | CONFIRMED that Google supports it; GBP-specific and web-server-flow statements UNVERIFIED | "Google supports the Proof Key for Code Exchange (PKCE) protocol to make the installed app flow more secure." https://developers.google.com/identity/protocols/oauth2/native-app ; the web-server page I read does not mention PKCE, and the GBP OAuth guide does not either. |
| Is business.manage a "sensitive" scope needing consent-screen verification | - | - | UNVERIFIED | The scopes page lists the scope without a sensitivity label; Google says "scope categories (non-sensitive, sensitive, or restricted) are indicated automatically in the Google Cloud Console" https://support.google.com/cloud/answer/9110914 . I could not read the console. Rule if it is sensitive: "Apps that request access to scopes categorized as sensitive or restricted must complete Google's OAuth app verification before being granted access." same URL. Check in Cloud Console > Data Access. |
| Privacy policy and consent screen | - | - | CONFIRMED | "You must publish a privacy policy that fully documents how your application interacts with user data." https://developers.google.com/terms/api-services-user-data-policy |
| Offline-use guidance | - | - | CONFIRMED | "It's recommended that platforms incorporate this functionality because you can edit, view, and manage listings anytime after the user has signed in" implement-oauth URL |

---

## 8. ACCESS FOR THIRD-PARTY CUSTOMERS

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Access is by approval, requested per Cloud project | GBP API contact form, "Application for Basic API Access" | project number | CONFIRMED | "Select 'Application for Basic API Access' from the drop-down menu and provide all requested information." https://developers.google.com/my-business/content/prereqs |
| Per project? | - | - | CONFIRMED (implied) | "Find your Project Number in the Project info card on your project's Dashboard. You will need to provide this number in your application." and "viewing the quotas ... If your quota is set to 300 QPM, your project is approved." PREREQ |
| Eligibility | - | - | CONFIRMED | "Manage a Google Business Profile that is verified and active for 60+ days." and "Have a website representing the business listed on the GBP." PREREQ |
| The qualifying profile can be a client's | - | - | CONFIRMED | "This GBP can be the applicant's own office or headquarters or it could belong to one of the clients they manage." PREREQ |
| Application email | - | - | CONFIRMED | "Make sure that you are using an email address that is listed as an owner/manager on your business's GBP." PREREQ |
| Approval signal | Cloud Console quotas | - | CONFIRMED | "If your quota is 0 QPM (Queries Per Minute), your project has not yet been approved." PREREQ |
| Stated review time | - | - | UNVERIFIED | None given. Only: "A follow-up email will be sent to you after your request has been reviewed." and a recommendation to keep the profile complete "for a smooth and timely review". PREREQ |
| Rejection / reapply | - | - | CONFIRMED | "If your application is rejected, make sure you have met the requirements stated earlier before re-applying." PREREQ |
| The API is hidden until approved | - | - | CONFIRMED | "The Google My Business API is only visible in the Google Cloud console to users who submit and receive approval" https://developers.google.com/my-business/content/basic-setup |
| Default quotas | - | - | CONFIRMED | Business Information "Default requests: 300 QPM"; Account Management "300 QPM"; Notifications "300 QPM"; Performance, Verifications, Lodging, Place Actions 300 QPM each; "Edits: 10 per minute per Google Business Profile (cannot be increased)". LIMITS. 429 on exceed. |
| QPM for the v4 Reviews/Local Posts/Media quota | `mybusiness.googleapis.com` | - | UNVERIFIED | The limits table has no row for the v4 "Google My Business API" service (reviews, posts, media). Likely 300 QPM like the rest (the 300 QPM approval signal in PREREQ), but not stated per API. Check Cloud Console quotas. |
| Quota increase | form | - | CONFIRMED | "Quota increase requests are typically denied if: ... Your average usage is less than 50% of your current QPM limit." LIMITS. "Don't request a quota increase" while quota is 0. |
| One project can serve many customers | policy | - | CONFIRMED | "If you provide the Business Profile APIs to end-clients or other third-party clients, you cannot require them to apply for their own Business Profile project in order to use the Business Profile APIs." POLICY |
| But no programmatic pass-through to customers | policy | - | CONFIRMED | "You cannot provide indirect access to your Business Profile project. End users of your Business Profile APIs need to manually sign in to use it." POLICY. Our own automation acting for a signed-in customer is allowed: "This policy doesn't restrict your own use of the Business Profile APIs in a programmatic or automated way." |
| Customer's location must be verified | - | - | CONFIRMED | updateReply: "This operation is only valid if the specified location is verified." (V4REV updateReply); help: "Important: Before you can reply to reviews, you must verify your business." https://support.google.com/business/answer/3474050 |
| Verification time (customer side) | help | - | CONFIRMED | "verification reviews take up to 5 business days." https://support.google.com/business/answer/7107242 |
| Demo account on request | policy | - | CONFIRMED | "you must provide a demo account to your API tool within seven days of the request." POLICY |
| Termination handling | policy | - | CONFIRMED | "you have seven business days to provide them the ability to disassociate their Business Profile account from your services" POLICY |
| Notify customer of account changes | policy | - | CONFIRMED | "provide notice to the end-client of the change within 48 hours after the change is made." POLICY |
| Project can be deactivated | policy | - | CONFIRMED | "Google may deactivate your API project ID if your project is not adhering to Google's API policies." POLICY |
| Do not copy Google UI / imply partnership | policy | - | CONFIRMED | "Your Business Profile APIs must not replicate the look and feel of the Business Profile user interface." POLICY |

---

## 9. Policy

| Action | Endpoint | Scope/permission | Status | Quote + URL |
|---|---|---|---|---|
| Automated review replies | policy | - | CONFIRMED (this is the key rule) | "you must not automate or trigger review replies, Q&As, listing creations, listing edits, or other actions without the user's prior specific and express consent." https://developers.google.com/my-business/content/policies . Implication: an auto-send category needs a recorded, specific, express opt-in from the customer for that action type. A general "I agree to the ToS" is a risk. |
| Reply authorization | policy | - | CONFIRMED | "If you respond to reviews on behalf of your end-client, you must receive their authorization first." same URL |
| Reply content rules | policy | - | CONFIRMED | "All responses to reviews must follow Google's Prohibited and restricted content policies." same URL |
| AI-generated reply rule | - | - | UNVERIFIED (no statement found) | I found no sentence in the policy, terms, or help pages that mentions AI, generated, or machine-written replies, either permitting or banning. The consent rule above is the only operative text. |
| Data retention / content storage | policy | - | CONFIRMED (this one is severe) | "You cannot pre-fetch, cache, index, or store any content provided through the Business Profile APIs ("Content") for use outside of your Business Profile project except for limited amounts of Content." and "It must be stored temporarily for no more than 30 calendar days." and "It cannot be manipulated or aggregated in any way." same URL. Needs legal review for a SaaS inbox that keeps review history. Whether our own draft/audit records count as "Content" is UNVERIFIED. |
| Storing to defeat usage tracking | policy | - | CONFIRMED | "At no time may you store Content in order to prevent Google from tracking usage of your Business Profile project." same URL |
| Deletion of customer data | policy | - | CONFIRMED (partly) | On offboarding: "you must also relinquish and remove those permissions accordingly." same URL. Review-text deletion on request: UNVERIFIED. |
| Sending review text to a third-party AI provider | - | - | UNVERIFIED (no statement found) | No GBP page names it. General rule (applies to Google user data via OAuth): "Do not misrepresent what data is collected or what you do with Google user data." and disclose "all user data that you access, use, store, delete, or share" https://developers.google.com/terms/api-services-user-data-policy . That page does not name Business Profile, so whether its Limited Use clause governs GBP is UNVERIFIED. Combined with the 30-day / "cannot be manipulated or aggregated" storage clause, passing review text to an LLM provider should be disclosed in our privacy policy and cleared with Google or counsel. |
| Order of precedence | terms | - | CONFIRMED | "the order of precedence ... (1) the Business Profile Terms, (2) the Business Profile API Policies, and (3) the Google APIs Terms of Service." https://developers.google.com/my-business/content/terms |
| Google may audit | policy | - | CONFIRMED | "We may review your use of the Business Profile APIs at any time and for any reason" POLICY |
| Google reviews replies post-hoc | help | - | CONFIRMED | "If your reply isn't approved to be posted, you'll be asked to edit it." https://support.google.com/business/answer/3474050 ; API surfaces `reviewReplyState` / `policyViolation` (list + batchGetReviews only, 2026-04-01 and 2026-07-01 changes). |
| Phone/regulated-content post rules | help | - | CONFIRMED | see section 4 |
| GoogleLocations endpoint abuse | policy | - | CONFIRMED | "Use of this endpoint for any other purpose, which includes lead generation or other analysis, is against Google's policies" POLICY (do not use `googleLocations:search` for prospecting) |

---

## 10. Open items - only a live call can settle

1. Exact Pub/Sub message body for NEW_REVIEW / UPDATED_REVIEW (field names, whether `data` is JSON, any attributes, whether rating/comment are ever included). Docs only name `review_name` and `location_name`.
2. Latency from review posted to Pub/Sub publish. No SLA documented.
3. Whether UPDATED_REVIEW fires when the owner replies, when the reviewer edits, or both. Needed to avoid reply loops.
4. Whether notificationSetting can be set by a Manager (not Owner) of the customer account; what error appears otherwise.
5. Whether one shared topic with many customer accounts works as expected (the docs say repeat per account; they do not say "shared topic is fine"), and how a message identifies the account (only the location/review resource names are documented).
6. Is `business.manage` flagged sensitive/restricted in our Cloud Console, and what Google's OAuth verification demands (brand verification, demo video, security assessment). Docs do not say.
7. v4 Reviews/Posts/Media per-API default QPM (limits table has no row for `mybusiness.googleapis.com`).
8. Review time for "Application for Basic API Access" (no stated time).
9. Local post `summary` max length; maximum photo pixel size; aspect ratio rules; whether video can be attached through the API (enum says photo only; help says video allowed).
10. Whether `reviews.list` returns reviews for locations that are unverified (docs say verified only; actual error shape unknown).
11. Access-token `expires_in` actual value for this scope.
12. PKCE: whether Google's token endpoint accepts `code_challenge` on a web-server (confidential) client flow for GBP. Supported on installed-app flow; web-server docs silent.
13. Legal: whether our stored review text, drafts and audit trail breach the 30-day storage clause, and whether sending review text to an LLM vendor is allowed. Ask Google through the API support form ("fill this form" is linked from the policy page) or counsel; do not assume.
14. How "prior specific and express consent" must be captured for auto-send categories (per category, per location, revocable?). Docs give no template.
15. Reply moderation outcome after a 200 (up to 30 days per help page): we must show PENDING/REJECTED state to the human and never mark "sent" on 200 alone. State comes back only via `reviews.list` / `batchGetReviews`, not `reviews.get`.

---

## URLs opened (and what I read)

GBP developer docs
- https://developers.google.com/my-business/reference/rest - service page, v4 and v1 resource lists, "(deprecated)" tags
- https://developers.google.com/my-business/content/overview - overview, usage-limits pointer (no version info)
- https://developers.google.com/my-business/content/prereqs - access request steps, 60-day verified profile rule, 300 QPM approval signal, no review time
- https://developers.google.com/my-business/content/notification-setup - Pub/Sub setup steps, publisher service account, update/get/delete endpoints
- https://developers.google.com/my-business/content/basic-setup - APIs to enable, OAuth client, no sandbox, Workspace 403
- https://developers.google.com/my-business/content/sunset-dates - deprecation schedule, Q&A discontinued 2025-11-03
- https://developers.google.com/my-business/content/limits - default quotas, quota-increase rules, "Pub/Sub... without polling"
- https://developers.google.com/my-business/content/pricing - no charge
- https://developers.google.com/my-business/content/latest-updates - 2026 change entries (review fields, recurring posts)
- https://developers.google.com/my-business/content/support - fetched, not relied on
- https://developers.google.com/my-business/content/policies - Business Profile APIs policies (automation consent, 30-day storage, third-party rules)
- https://developers.google.com/my-business/content/terms - order of precedence
- https://developers.google.com/my-business/content/implement-oauth - scopes, offline access, plus.business.manage deprecated
- https://developers.google.com/my-business/content/review-data - review how-to (list/get/batch/reply/delete, get limitation, Workspace admin note)
- https://developers.google.com/my-business/content/account-data and /location-data, /change-log - skimmed (location-data used to confirm v1 location API)
- https://developers.google.com/my-business/content/notifications/change-log - Notifications v1 endpoint, Q&A types deprecated
- https://developers.google.com/my-business/reference/accountmanagement/rest, .../v1/accounts, .../v1/accounts/list - Account Management v1
- https://developers.google.com/my-business/reference/businessinformation/rest, .../v1/accounts.locations/list, .../v1/locations - Business Information v1
- https://developers.google.com/my-business/reference/notifications/rest, .../v1/NotificationSetting, .../v1/accounts/updateNotificationSetting - Notifications API
- https://developers.google.com/my-business/reference/rest/v4/Notifications - old v4 notification type (shows older enum incl. UPDATED_LOCATION_STATE)
- https://developers.google.com/my-business/reference/rest/v4/accounts, /accounts.locations - method lists with deprecated tags
- https://developers.google.com/my-business/reference/rest/v4/accounts.locations/list, /batchGetReviews
- https://developers.google.com/my-business/reference/rest/v4/accounts.locations.reviews, /list, /updateReply, /deleteReply
- https://developers.google.com/my-business/reference/rest/v4/accounts.locations.localPosts, /create, /list
- https://developers.google.com/my-business/reference/rest/v4/accounts.locations.media, /create, /startUpload
- https://developers.google.com/my-business/reference/media/rest/v1/media/upload - binary upload URI
- https://developers.google.com/my-business/reference/verifications/rest/v1/locations/getVoiceOfMerchantState

Google identity / OAuth
- https://developers.google.com/identity/protocols/oauth2 - refresh token expiry causes, 100-token cap, Testing = 7 days
- https://developers.google.com/identity/protocols/oauth2/web-server - access_type=offline, refresh token validity, expires_in
- https://developers.google.com/identity/protocols/oauth2/native-app - PKCE support
- https://developers.google.com/identity/protocols/oauth2/scopes - business.manage description
- https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification - generic verification rules
- https://developers.google.com/identity/protocols/oauth2/production-readiness/policy-compliance - fetched, not relied on
- https://developers.google.com/terms/api-services-user-data-policy - disclosure/privacy rules, Limited Use
- https://support.google.com/cloud/answer/9110914 - OAuth app verification help (categories shown in Console)
- https://support.google.com/cloud/answer/10311615 and /7454865 - fetched, not relied on

Business Profile Help
- https://support.google.com/business/answer/6103862 - photo/video format, size, resolution rules
- https://support.google.com/business/answer/7342169 - posts types and statuses, content rules
- https://support.google.com/business/answer/3474050 - replying to reviews, moderation timing, verification
- https://support.google.com/business/answer/7107242 - verification timing (up to 5 business days)
- (404, not read) https://support.google.com/business/answer/7339882 and /9230568

Google Cloud Pub/Sub
- https://cloud.google.com/pubsub/docs/push - HTTPS POST push, ack codes, backoff, wrapped body
- https://cloud.google.com/pubsub/docs/authenticate-push-subscriptions - OIDC JWT, serviceAccountTokenCreator, claims
- https://cloud.google.com/pubsub/docs/create-push-subscription - HTTPS + valid SSL certificate endpoint requirement
- https://cloud.google.com/pubsub/docs/subscription-overview - at-least-once delivery
- https://cloud.google.com/pubsub/docs/subscription-properties - retention 7 days default, 10 min to 31 days
- https://cloud.google.com/pubsub/docs/overview, /subscriber, /exactly-once-delivery, /handling-failures - fetched; the guarantee quote came from subscription-overview

Method note: pages were read as extracted page text (WebFetch summaries and direct HTTP GETs stripped to text). Quotes in this file were copied from the raw text extracts, not from summaries, except where marked as from the notification-setup page's first read (setup steps, which I re-verified in the raw text).
